using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using iTextSharp.text;
using iTextSharp.text.error_messages;
using iTextSharp.text.pdf;
using XfaBuilder;

/* 
 * Copyright (c) 2015 John Dziurlaj
 */
namespace XfaPdfBuilder
{
    /* A PDF reader may ignore any value which is a single stream as that form is deprecated. 
     * Use pf Array is thus strongly recommended */
    enum LayoutStyle { Stream, Array }

    class ShellXdpPdf
    {
        private Stream outputStream;
        private Document shellDocument;
        public Document ShellDocument
        { get { return shellDocument; } }
        //the absolute path from which relative paths can be resolved
     
        public string resolverPath { get; set; }        
        private PdfWriter writer;
        public PdfWriter Writer { get { return writer; } }
        
        /* we have chosen to support the creation as a array containing the specific packets instead of a single
         * stream, but it may actually be easier for users to work with the single stream, as it would allow
         * a copy / paste from Designer with no additional parsing effort.
         */
        public LayoutStyle style = LayoutStyle.Array;

        public const String XFA_DATA_SCHEMA = "http://www.xfa.org/schema/xfa-data/1.0/";
        public const String XDP_SCHEMA = "http://ns.adobe.com/xdp/";
        /* 2.8 is the version created by Designer ES2 */
        public const String XFA_TEMPLATE_SCHEMA_2_8 = "http://www.xfa.org/schema/xfa-template/2.8/";
        public const String XFA_CONFIG_SCHEMA_2_8 = "http://www.xfa.org/schema/xci/2.8/";
        public const String XFA_LOCALE_SCHEMA_2_7 = "http://www.xfa.org/schema/xfa-locale-set/2.7/";

        /** Packets recognized by the XFA 3.3 Specification */
        public const String CONFIG = "config";
        public const String CONNECTION_SET = "connectionSet";
        public const String DATASETS = "datasets";
        public const String LOCALE_SET = "localeSet";
        public const String PDF = "pdf";
        public const String SIGNATURE = "signature";
        public const String SOURCE_SET = "sourceSet";
        public const String STYLESHEET = "stylesheet";
        public const String TEMPLATE = "template";
        public const String XDC = "xdc";
     
        /** Packets seen in the wild */
        public const String XMPMETA = "xmpmeta";
        public const String XFDF = "xfdf";
        public const String FORM = "form";

        /** Represents the beginning and end tags for the xdp:xdp element  */
        public const String PREAMBLE = "preamble";
        public const String POSTAMBLE = "postamble";


        /* If a user wants to work with a stream instead of an array they can set this */
        private XmlDocument package;        
        /* Contains all the XFA Packets */
        private List<KeyValuePair<string,XmlDocument>> XfaPackets;
        /* These packets must be handled separately as their order matters*/
        private string preamble { get; set; }
        private string postamble { get; set; }

        /* Non packet data */
        private XmlDocument xmpMeta { get; set; }
        
        private PdfStream metadataPs;   

        PdfString xdpStr = new PdfString("preamble");
        PdfString configStr = new PdfString(CONFIG);
        PdfString templateStr = new PdfString(TEMPLATE);
        PdfString localeSetStr = new PdfString(LOCALE_SET);
        PdfString xmpmetaStr = new PdfString(XMPMETA);
        PdfString xfdfStr = new PdfString(XFDF);
        PdfString formStr = new PdfString(FORM);
        PdfString datasetsStr = new PdfString(DATASETS);
        PdfString closexdpStr = new PdfString("postamble");

        /// <summary>
        /// Create a Shell PDF. This type of PDF contains all
        /// XFA data inside the XFA dictionary entry.
        /// A PDF "boilerplate" is displayed if the viewer
        /// doesn't understand XFA.
        /// Limitations        
        /// 2. Does not support embedded fonts
        /// 3. Does not support fonts other than TrueType
        /// Does not support tagged documents (StructTreeRoot)
        /// </summary>
        /// <param name="output"></param>
        public ShellXdpPdf(Stream output)
        {
            // create a place to store our streams
            XfaPackets = new List<KeyValuePair<String, XmlDocument>>();
            shellDocument = new Document();
            outputStream = output;

            writer = /*new PdfCopy(shellDocument, output);*/PdfCopy.GetInstance(shellDocument, output);
            
            //LiveCycle Designer uses Full Compression so do we!
            writer.SetFullCompression();

            writer.ExtraCatalog.Put(new PdfName("NeedsRendering"), new PdfBoolean(true));
            AddPdfMetaData();
            AddExtensionsDictionary();
            ShellDocument.SetPageSize(PageSize.LETTER);
            //ShellDocument.SetMargins(PageSize.LETTER.Left, PageSize.LETTER.Right, PageSize.LETTER.Top, PageSize.LETTER.Bottom);
            
            shellDocument.Open();
            writer.Open();
        }

        private void AddPdfMetaData()
        {
            shellDocument.AddCreator("Adobe LiveCycle Designer 11.0");
            //ModDate gets added automatically
            shellDocument.AddCreationDate();
            shellDocument.AddProducer();
        }

        public void Close()
        {
            var acroForm = new PdfDictionary();
            var xfaArr = new PdfArray();

            acroForm.Put(PdfName.DA, new PdfString("/Helv 0 Tf 0 g "));

            //TODO don't assume!
            var templateXmlDocument = XfaPackets.Single(o => o.Key == "template").Value;
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(templateXmlDocument.NameTable);
            //must provide namespace!
            //http://msdn.microsoft.com/en-us/library/e5t11tzt%28v=vs.110%29.aspx
            nsmgr.AddNamespace("template", "http://www.xfa.org/schema/xfa-template/2.8/");
            //http://www.xfa.org/schema/xfa-template/3.3/ (ES4)
            #region XFA External Resources
            ResolveExternals(ref templateXmlDocument, nsmgr);
            #endregion

            #region XFA Fonts
            if (templateXmlDocument != null)
            {
                //we have to create a at least one AcroForm Field, so when
                //IsValid is called, the catalog entries will get created
                //var afHiddenField = writer.AcroForm.AddHiddenField("1", "2");
                //we are using appearance as a method to seed the /DR dictionary
                // with fonts
                var appearance = PdfAppearance.CreateAppearance(writer, 0, 0);
                //get all fonts from the XDP template packet
                var fontNodes = templateXmlDocument.SelectNodes("/template:template/*//template:font/@typeface", nsmgr);

                var innerfontDictionary = new PdfDictionary();
                FontFactory.RegisterDirectories();
                foreach (XmlNode fontNode in fontNodes)
                {
                    //add font to catalog
                    appearance.SetFontAndSize(FontFactory.GetFont(fontNode.Value, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED).BaseFont, 0);
                    TrueTypeFont ttf;
                    PdfIndirectReference dicRef;
                    dicRef = WriteTTF(fontNode.Value, writer, out ttf);
                    innerfontDictionary.Put(new PdfName(ttf.PostscriptFontName), dicRef);
                }
                /*  {
                      TrueTypeFont ttf;
                      PdfIndirectReference dicRef;
                      dicRef = WriteTTF(BaseFont.HELVETICA, writer, out ttf);
                      innerfontDictionary.Put(new PdfName(ttf.PostscriptFontName), dicRef);

                  }*/
                var outerfontDictionary = new PdfDictionary();
                outerfontDictionary.Put(new PdfName("Font"), innerfontDictionary);

                acroForm.Put(new PdfName("DR"), outerfontDictionary);
                //afHiddenField.SetAppearance(new PdfName("just_for_appearances"), appearance);
            }
            #endregion

            //var templ = PdfTemplate.CreateTemplate(writer, 0, 0);
            //var font = FontFactory.GetFont("Arial");
            //templ.SetFontAndSize(font.BaseFont, 12);                                                
            #region XFA Generation
            if (style == LayoutStyle.Stream)
            {
                if (package != null)
                {
                    byte[] bytes = System.Text.Encoding.ASCII.GetBytes(package.InnerXml);
                    var currentPs = new PdfStream(bytes);
                    currentPs.FlateCompress(writer.CompressionLevel);
                    var curRef = writer.AddToBody(currentPs).IndirectReference;
                    PdfString curStr = new PdfString("preamble");
                    acroForm.Put(new PdfName("XFA"), curRef);
                    Console.WriteLine(String.Format("Writing out stream {0}: {1} bytes", "XFA", bytes.Length));
                }
                else
                {
                    var streamStr = new StringBuilder();
                    streamStr.Append(preamble);
                    foreach (var packet in XfaPackets)
                    {
                        streamStr.Append(packet.Value.InnerXml);
                    }
                    streamStr.Append(postamble);
                    throw new NotImplementedException("Software does not currently know how to convert from Array to Stream format.");
                }
            }
            else
            {
                if (package != null)
                {
                    throw new NotImplementedException("Software does not currently know how to convert from Stream to Array format.");
                }
                else
                {
                    {
                        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(preamble);
                        var currentPs = new PdfStream(bytes);
                        currentPs.FlateCompress(writer.CompressionLevel);
                        var curRef = writer.AddToBody(currentPs).IndirectReference;
                        PdfString curStr = new PdfString("preamble");
                        xfaArr.Add(curStr);
                        xfaArr.Add(curRef);
                        Console.WriteLine(String.Format("Writing out packet {0}: {1} bytes", "preamble", bytes.Length));
                    }
                    /* The packets can appear in any order */
                    foreach (var packet in XfaPackets)
                    {
                        //TODO - do a raw byte copy
                        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(packet.Value.InnerXml);
                        var currentPs = new PdfStream(bytes);
                        currentPs.FlateCompress(writer.CompressionLevel);
                        var curRef = writer.AddToBody(currentPs).IndirectReference;
                        PdfString curStr = new PdfString(packet.Key);
                        xfaArr.Add(curStr);
                        xfaArr.Add(curRef);
                        Console.WriteLine(String.Format("Writing out packet {0}: {1} bytes", packet.Key, bytes.Length));
                    }
                    {
                        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(postamble);
                        var currentPs = new PdfStream(bytes);
                        currentPs.FlateCompress(writer.CompressionLevel);
                        var curRef = writer.AddToBody(currentPs).IndirectReference;
                        PdfString curStr = new PdfString("preamble");
                        xfaArr.Add(curStr);
                        xfaArr.Add(curRef);
                        Console.WriteLine(String.Format("Writing out packet {0}: {1} bytes", "postamble", bytes.Length));

                    }
                }
                acroForm.Put(new PdfName("XFA"), xfaArr);
            }
            #endregion

            #region XmpMetadata
            if(xmpMeta != null)
            {
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(xmpMeta.InnerXml);
                writer.XmpMetadata = bytes;
            }

            #endregion
            //wire it up


            //we are not putting anything here
            //but Adobe expects it
            var fieldsPa = new PdfArray();

            acroForm.Put(new PdfName("Fields"), fieldsPa);

            writer.ExtraCatalog.Put(new PdfName("AcroForm"), acroForm);
            if (metadataPs != null)
            {
                var MetadataRef = writer.AddToBody(metadataPs).IndirectReference;
                writer.ExtraCatalog.Put(new PdfName("Metadata"), MetadataRef);
            }

            shellDocument.Close();

            writer.Close();
        }

        private void ResolveExternals(ref System.Xml.XmlDocument templateXmlDocument, XmlNamespaceManager nsmgr)
        {
            if (templateXmlDocument != null)
            {
                var nodes = templateXmlDocument.SelectNodes("/template:template/*//template:image/@href|/template:template/*//template:exObject[@codeType='application/x-shockwave-flash']/@archive", nsmgr);
                                                
                if (nodes.Count > 0)
                {
                    var xfaAnnonDic = new PdfDictionary();
                    var xfaImagesDic = new PdfDictionary();
                    var namesArr = new PdfArray();
                    xfaImagesDic.Put(new PdfName("Names"), namesArr);                    
                    
                    //note this implementation won't scale well                    
                    List<string> uris = new List<string>();
                    foreach (XmlNode node in nodes)
                    {
                        if (uris.Any(o => o == node.Value))
                            continue;
                        else
                            uris.Add(node.Value);
                    }
                

                    foreach (var uri in uris)
                    {
                        try
                        {
                            string path;
                            if (resolverPath != null && uri.StartsWith("."))
                            {
                                //sometimes XFA uris will contain relative paths
                                path = Path.Combine(resolverPath, uri);
                            }
                            else
                            {
                                //assume path is absolute
                                path = uri;
                            }
                            //try to resolve local reference
                            var imageFs = File.OpenRead(path);
                            var xdpReader = new BinaryReader(imageFs);
                            byte[] xdpBytes = xdpReader.ReadBytes((int)imageFs.Length);
                            var imagePs = new PdfStream(xdpBytes);
                            imagePs.FlateCompress(writer.CompressionLevel);
                            var imageRef = writer.AddToBody(imagePs).IndirectReference;
                            //add name to array
                            namesArr.Add(new PdfString(uri));
                            //then add a reference to our stream
                            namesArr.Add(imageRef);
                        }
                        catch(Exception e)
                        {
                            //need to find a way to make a note of this, we'll continue
                            throw e;
                        }
                    }

                    var xfaImagesRef = writer.AddToBody(xfaImagesDic).IndirectReference;
                    xfaAnnonDic.Put(new PdfName("XFAImages"), xfaImagesRef);
                    var xfaAnnonDicRef = writer.AddToBody(xfaAnnonDic).IndirectReference;
                    //note that if named destinations are added, this will not work!
                    writer.ExtraCatalog.Put(new PdfName("Names"), xfaAnnonDicRef);
                }
            }
        }

        private void AddExtensionsDictionary()
        {
            var adbeDic = new PdfDictionary();
            adbeDic.Put(new PdfName("BaseVersion"), new PdfName("1.7"));
            adbeDic.Put(new PdfName("ExtensionLevel"), new PdfNumber(8));
            var extensionsDic = new PdfDictionary();
            extensionsDic.Put(new PdfName("ADBE"), adbeDic);
            writer.ExtraCatalog.Put(new PdfName("Extensions"), extensionsDic);
        }
        #region StreamSetters
        public void SetPackage(XmlDocument doc)
        {
            this.package = doc;
        }

        public void SetPreamble(string preamble)
        {
            this.preamble = preamble;
          /*  if (XFANodes.ContainsKey("preamble"))
                XFANodes.Remove("preamble");
            else
                XFANodes.Add("preamble", doc);*/
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetConfig(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == CONFIG);
            XfaPackets.Add(new KeyValuePair<string,XmlDocument>(CONFIG, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetConnectionSet(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == CONNECTION_SET);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(CONNECTION_SET, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetDatasets(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == DATASETS);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(DATASETS, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetLocaleSet(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == LOCALE_SET);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(LOCALE_SET, packet));                
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetForm(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == FORM);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(FORM, packet));
        }
        public void SetMetaData(Stream metadataFs)
        {
            var MetadataReader = new BinaryReader(metadataFs);
            byte[] MetadataBytes = MetadataReader.ReadBytes((int)metadataFs.Length);
            metadataPs = new PdfStream(MetadataBytes);
            metadataPs.FlateCompress(writer.CompressionLevel);
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetPdf(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == PDF);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(PDF, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetSourceSet(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == SOURCE_SET);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(SOURCE_SET, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetStyleSheet(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == STYLESHEET);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(STYLESHEET, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packets
        /// </summary>
        /// <param name="packet">Packets to add</param>
        public void SetStyleSheet(XmlDocument[] packets)
        {
            XfaPackets.RemoveAll(o => o.Key == STYLESHEET);
            foreach(var packet in packets)
                XfaPackets.Add(new KeyValuePair<string, XmlDocument>(STYLESHEET, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetTemplate(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == TEMPLATE);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(TEMPLATE, packet));                 
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetXdc(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == XDC);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(XDC, packet));
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetXfdf(XmlDocument packet)
        {
            XfaPackets.RemoveAll(o => o.Key == XFDF);
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(XFDF, packet));             
        }
        /// <summary>
        /// Sets or replaces the XMP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetXmpMeta(XmlDocument metaData)
        {
            this.xmpMeta = metaData;
           // XFANodes.RemoveAll(o => o.Key == XMPMETA);
           // XFANodes.Add(new KeyValuePair<string, XmlDocument>(XMPMETA, packet));                       
        }
        /// <summary>
        /// Adds a custom packet to the Package
        /// </summary>
        /// <param name="packetName">Name of the packet to add</param>
        /// <param name="packet">Packet to add</param>
        public void AddCustomPacket(string packetName, XmlDocument packet)
        {
            XfaPackets.Add(new KeyValuePair<string, XmlDocument>(packetName, packet));  
        }
        /// <summary>
        /// Sets or replaces the XDP Packet
        /// </summary>
        /// <param name="packet">Packet to add</param>
        public void SetPostamble(string postamble)
        {
            this.postamble = postamble;
            //XFANodes.Add("postamble", packet);
        }        
        #endregion
        private static PdfIndirectReference WriteTTF(string fontName, PdfWriter writer, out TrueTypeFont ttf)
        {
            var fontobj = FontFactory.GetFont(fontName);
            ttf = (TrueTypeFont)fontobj.BaseFont;
            //BaseFont.CreateFont(fontName, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);

            int firstChar = 0;
            int lastChar = 255;
            byte[] shortTag = new byte[256];
            bool subsetp = false;
            if (!subsetp)
            {
                firstChar = 0;
                lastChar = shortTag.Length - 1;
                for (int k = 0; k < shortTag.Length; ++k)
                    shortTag[k] = 1;
            }
            PdfIndirectReference ind_font = null;
            PdfObject pobj = null;
            PdfIndirectObject obj = null;
            string subsetPrefix = "";

            pobj = ttf.GetFontDescriptor(ind_font, subsetPrefix, null);
            if (pobj != null)
            {
                obj = writer.AddToBody(pobj);
                ind_font = obj.IndirectReference;
            }
            //TODO CHANGE ME!
            string style = "";

            PdfDictionary dic = new PdfDictionary(PdfName.FONT);
            if (ttf.Cff)
            {
                dic.Put(PdfName.SUBTYPE, PdfName.TYPE1);
                dic.Put(PdfName.BASEFONT, new PdfName(ttf.PostscriptFontName + style));
            }
            else
            {
                dic.Put(PdfName.SUBTYPE, PdfName.TRUETYPE);
                dic.Put(PdfName.BASEFONT, new PdfName(subsetPrefix + ttf.PostscriptFontName + style));
            }
            dic.Put(PdfName.BASEFONT, new PdfName(subsetPrefix + ttf.PostscriptFontName + style));

            if (ttf.Encoding.Equals(BaseFont.CP1252) || ttf.Encoding.Equals(BaseFont.MACROMAN))
                dic.Put(PdfName.ENCODING, ttf.Encoding.Equals(BaseFont.CP1252) ? PdfName.WIN_ANSI_ENCODING : PdfName.MAC_ROMAN_ENCODING);

            dic.Put(PdfName.FIRSTCHAR, new PdfNumber(firstChar));
            dic.Put(PdfName.LASTCHAR, new PdfNumber(lastChar));
            PdfArray wd = new PdfArray();
            for (int k = firstChar; k <= lastChar; ++k)
            {
                if (shortTag[k] == 0)
                    wd.Add(new PdfNumber(0));
                else
                    wd.Add(new PdfNumber(ttf.Widths[k]));
            }
            dic.Put(PdfName.WIDTHS, wd);
            if (ind_font != null)
                dic.Put(PdfName.FONTDESCRIPTOR, ind_font);

            return writer.AddToBody(dic).IndirectReference;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            {
                var img = new FileStream("C:\\Users\\jdziu_000\\Desktop\\pdf\\7.0\\empty static.pdf", FileMode.Open);
                var reader = new BinaryReader(img);
                var pdfpack = new PdfPacket(reader.ReadBytes((int)img.Length));
                Console.WriteLine(pdfpack.Packet.InnerXml);
                Console.ReadKey();
            }


            var fs = new FileStream("C:\\Users\\jdziu_000\\Desktop\\pdf\\emi.pdf", FileMode.Create);
            var shell = new ShellXdpPdf(fs);
            //set location where external references (in XDP packets) can be found
            shell.resolverPath = "C:\\Users\\jdziu_000\\Desktop\\pdf";

            var currentMethod = LayoutStyle.Array;
                                    
             /* PdfReader pr = new PdfReader("c:\\temp\\Logical.pdf");
              for(int i = 1; i <= pr.NumberOfPages; i++)
             {
                 var copier = shell.Writer as PdfCopy;
                 copier.AddPage(shell.Writer.GetImportedPage(pr, i));
             }*/
            
            var font  = new Font(iTextSharp.text.Font.FontFamily.TIMES_ROMAN);
            var para = new Paragraph("Please wait...", font);                      
            
            shell.ShellDocument.Add(para);
            //does nothing, but write it out, to match ES4 output
            shell.Writer.AddPageDictEntry(PdfName.ROTATE, new PdfNumber(0));
            shell.Writer.AddPageDictEntry(PdfName.CROPBOX, new PdfRectangle(PageSize.LETTER));

            //template, datasets and config packets are always required!            
            if (currentMethod == LayoutStyle.Stream)
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.Load("c:\\temp\\xfabuilder\\xdp");
                shell.SetPackage(xmlDoc);

            }
            else
            {
                //{
                //    var reader = new StreamReader(new FileStream("c:\\temp\\xfabuilder\\0100_VersChkStrings", FileMode.Open));
                //    shell.Writer.AddJavaScript("!ADBE::0100_VersChkStrings", reader.ReadToEnd());
                //}
                //{
                //    var reader = new StreamReader(new FileStream("c:\\temp\\xfabuilder\\0100_VersChkVars", FileMode.Open));
                //    shell.Writer.AddJavaScript("!ADBE::0100_VersChkVars", reader.ReadToEnd());
                //}
                //{
                //    var reader = new StreamReader(new FileStream("c:\\temp\\xfabuilder\\0200_VersChkCode_XFACheck", FileMode.Open));
                //    shell.Writer.AddJavaScript("!ADBE::0200_VersChkCode_XFACheck", reader.ReadToEnd());
                //}
                {
                    var reader = new StreamReader(new FileStream("c:\\temp\\xfabuilder\\preamble", FileMode.Open));
                    shell.SetPreamble(reader.ReadToEnd());
                }
              /*  {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load("c:\\temp\\xfabuilder\\config");
                    shell.SetConfig(xmlDoc);
                }*/
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load("c:\\temp\\xfabuilder\\template");
                    shell.SetTemplate(xmlDoc);
                }
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load("c:\\temp\\xfabuilder\\localeSet");
                    shell.SetLocaleSet(xmlDoc);
                }
                {
                    var reader = new StreamReader(new FileStream("c:\\temp\\xfabuilder\\postamble", FileMode.Open));
                    shell.SetPostamble(reader.ReadToEnd());
                }
                //non XFA Packets
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load("c:\\temp\\xfabuilder\\xmpmeta_lp");
                    shell.SetXmpMeta(xmlDoc);
                }             
            }
            shell.Close();
            Console.ReadKey();
        }
    }
}