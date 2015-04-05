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

/* 
 * Copyright (c) 2015 John Dziurlaj
 */
namespace XfaPdfBuilder
{
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
        
        /* we have chosen to handle the creation as a array containing the specific packets instead of a single
         * stream, but it may actually be easier for users to work with the single stream, as it would allow
         * a copy / paste from Designer with no additional parsing effort.
         */

        public const String XFA_DATA_SCHEMA = "http://www.xfa.org/schema/xfa-data/1.0/";
        public const String XDP_SCHEMA = "http://ns.adobe.com/xdp/";
        /* 2.8 is the version created by Designer ES2 */
        public const String XFA_TEMPLATE_SCHEMA_2_8 = "http://www.xfa.org/schema/xfa-template/2.8/";
        public const String XFA_CONFIG_SCHEMA_2_8 = "http://www.xfa.org/schema/xci/2.8/";
        public const String XFA_LOCALE_SCHEMA_2_7 = "http://www.xfa.org/schema/xfa-locale-set/2.7/";

        //New style
        private XmlDocument package;        
        private Dictionary<String, XmlDocument> XFANodes;



        private PdfStream metadataPs;
        private PdfStream datasetsPs;
        /* Container for the xdp package opening tag */
        private PdfStream xdpPs;
        /* Container for the config packet in a xdp package. This is a core packet per ISO 32000-2 / LCDES2 */
        private PdfStream configPs;
        /* Container for the template packet in a xdp package. This is a core packet per ISO 32000-2 / LCDES2 */
        private PdfStream templatePs;
        private byte[] templateBytes;
        /* Container for the locale packet in a xdp package. This is a core packet per LCDES2 */
        private PdfStream localeSetPs;
        /* Container for the locale packet in a xdp package. This is a core packet per LCDES2 */
        private PdfStream xmpmetaPs;
        private PdfStream xfdfPs;
        private PdfStream formPs;
        /* Container for the xdp package closing tag */
        private PdfStream closexdpPs;
        PdfString xdpStr = new PdfString("preamble");
        PdfString configStr = new PdfString("config");
        PdfString templateStr = new PdfString("template");
        PdfString localeSetStr = new PdfString("localeSet");
        PdfString xmpmetaStr = new PdfString("xmpmeta");
        PdfString xfdfStr = new PdfString("xfdf");
        PdfString formStr = new PdfString("form");
        PdfString datasetsStr = new PdfString("datasets");
        PdfString closexdpStr = new PdfString("postamble");

        /// <summary>
        /// Create a Shell PDF. This type of PDF contains all
        /// XFA boilerplate inside the XFA stream.
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
            XFANodes = new Dictionary<String, XmlDocument>();
            shellDocument = new Document();
            outputStream = output;
            
            writer = PdfWriter.GetInstance(shellDocument, output);
            
            //LiveCycle Designer uses Full Compression so do we!
            writer.SetFullCompression();

            writer.ExtraCatalog.Put(new PdfName("NeedsRendering"), new PdfBoolean(true));
            AddMetaData();
            AddExtensionsDictionary();
            shellDocument.Open();
            writer.Open();
        }

        private void AddMetaData()
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
            var templateXmlDocument = XFANodes["template"];

            //var templateXmlDocument = new XmlDocument();
            //templateXmlDocument.Load(new MemoryStream(templateBytes));
            XmlNamespaceManager nsmgr = new XmlNamespaceManager(templateXmlDocument.NameTable);
            //must provide namespace!
            //http://msdn.microsoft.com/en-us/library/e5t11tzt%28v=vs.110%29.aspx
            nsmgr.AddNamespace("template", "http://www.xfa.org/schema/xfa-template/2.8/");
            //http://www.xfa.org/schema/xfa-template/3.3/ (ES4)
            #region XFA External Resources
            //ResolveExternals(ref templateXmlDocument, nsmgr);
            #endregion

            #region XFA Fonts
            if (templatePs != null)
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
                /*{
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

            if(package != null)
            {
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(package.InnerXml);
                var currentPs = new PdfStream(bytes);
                currentPs.FlateCompress(writer.CompressionLevel);
                var curRef = writer.AddToBody(currentPs).IndirectReference;
                PdfString curStr = new PdfString("preamble");
                xfaArr.Add(curStr);
                xfaArr.Add(curRef);
            }

            foreach(var packet in XFANodes)
            {
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(packet.Value.InnerXml);                
                var currentPs = new PdfStream(bytes);
                currentPs.FlateCompress(writer.CompressionLevel);
                var curRef = writer.AddToBody(currentPs).IndirectReference;
                PdfString curStr = new PdfString(packet.Key);
                xfaArr.Add(curStr);
                xfaArr.Add(curRef);
            }

            /*if (xdpPs != null)
            {
                var xdpRef = writer.AddToBody(xdpPs).IndirectReference;
                xfaArr.Add(xdpStr);
                xfaArr.Add(xdpRef);
            }
            if (configPs != null) 
            {
                var configRef = writer.AddToBody(configPs).IndirectReference;
                xfaArr.Add(configStr);
                xfaArr.Add(configRef);
            }
            if (templatePs != null)
            {
                var templateRef = writer.AddToBody(templatePs).IndirectReference;
                xfaArr.Add(templateStr);
                xfaArr.Add(templateRef);
            }
            if (localeSetPs != null)
            {
                var localesetRef = writer.AddToBody(localeSetPs).IndirectReference;
                xfaArr.Add(localeSetStr);
                xfaArr.Add(localesetRef);
            }
            if (xmpmetaPs != null)
            {
                var xmpmetaRef = writer.AddToBody(xmpmetaPs).IndirectReference;            
                xfaArr.Add(xmpmetaStr);
                xfaArr.Add(xmpmetaRef);
            }
            if (xfdfPs != null)
            {
                var xfdfRef = writer.AddToBody(xfdfPs).IndirectReference;            
                xfaArr.Add(xfdfStr);
                xfaArr.Add(xfdfRef);
            }
            if (formPs != null)
            {
                var formRef = writer.AddToBody(formPs).IndirectReference;
                xfaArr.Add(formStr);
                xfaArr.Add(formRef);
            }
            if (datasetsPs != null) 
            {
                var datasetsRef = writer.AddToBody(datasetsPs).IndirectReference;
                xfaArr.Add(datasetsStr);
                xfaArr.Add(datasetsRef);
            }

            if (closexdpPs != null)
            {
                var closexdpRef = writer.AddToBody(closexdpPs).IndirectReference;
                xfaArr.Add(closexdpStr);
                xfaArr.Add(closexdpRef);
            }
             * */
            //wire it up
            
            acroForm.Put(new PdfName("XFA"), xfaArr);
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
            if (templatePs != null)
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

        public void SetMetaData(Stream metadataFs)
        {
            var MetadataReader = new BinaryReader(metadataFs);
            byte[] MetadataBytes = MetadataReader.ReadBytes((int)metadataFs.Length);
            metadataPs = new PdfStream(MetadataBytes);
            metadataPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetPackage(XmlDocument node)
        {
          this.package = node;
        }

        public void SetPreamble(Stream xdpFs)
        {
            var xdpReader = new BinaryReader(xdpFs);
            byte[] xdpBytes = xdpReader.ReadBytes((int)xdpFs.Length);
            xdpPs = new PdfStream(xdpBytes);
            xdpPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetConfig(XmlDocument packet)
        {
            XFANodes.Add("config", packet);
        }

        public void SetConfig(Stream configFs)
        {            
            var configReader = new BinaryReader(configFs);
            byte[] configBytes = configReader.ReadBytes((int)configFs.Length);
            configPs = new PdfStream(configBytes);
            configPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetTemplate(XmlDocument packet)
        {
            XFANodes.Add("template", packet);
        }
        
        public void SetTemplate(Stream templateFs)
        {
            var templateReader = new BinaryReader(templateFs);
            templateBytes = templateReader.ReadBytes((int)templateFs.Length);
            templatePs = new PdfStream(templateBytes);
            templatePs.FlateCompress(writer.CompressionLevel);
        }

        public void SetLocaleSet(Stream localeSetFs)
        {
            var localeSetReader = new BinaryReader(localeSetFs);
            byte[] localeSetBytes = localeSetReader.ReadBytes((int)localeSetFs.Length);
            localeSetPs = new PdfStream(localeSetBytes);
            localeSetPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetXmpMeta(Stream xmpmetaFs)
        {
            var xmpmetaReader = new BinaryReader(xmpmetaFs);
            byte[] xmpmetaBytes = xmpmetaReader.ReadBytes((int)xmpmetaFs.Length);
            xmpmetaPs = new PdfStream(xmpmetaBytes);
            xmpmetaPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetXfdf(Stream xfdfFs)
        {
            var xfdfReader = new BinaryReader(xfdfFs);
            byte[] xfdfBytes = xfdfReader.ReadBytes((int)xfdfFs.Length);
            xfdfPs = new PdfStream(xfdfBytes);
            xfdfPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetForm(Stream formFs)
        {
            var formReader = new BinaryReader(formFs);
            byte[] formBytes = formReader.ReadBytes((int)formFs.Length);
            formPs = new PdfStream(formBytes);
            formPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetDatasets(Stream datasetsFs)
        {
            var datasetsReader = new BinaryReader(datasetsFs);
            byte[] datasetsBytes = datasetsReader.ReadBytes((int)datasetsFs.Length);
            datasetsPs = new PdfStream(datasetsBytes);
            datasetsPs.FlateCompress(writer.CompressionLevel);
        }

        public void SetPostamble(XmlElement node)
        {
           // this.postamble = node;
        }

        public void SetPostamble(Stream closexdpFs)
        {
            var closexdpReader = new BinaryReader(closexdpFs);
            byte[] closexdpBytes = closexdpReader.ReadBytes((int)closexdpFs.Length);
            closexdpPs = new PdfStream(closexdpBytes);
            closexdpPs.FlateCompress(writer.CompressionLevel);
        }
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
            var fs = new FileStream("C:\\Users\\John\\Desktop\\pdf\\emi.pdf", FileMode.Create);
            var shell = new ShellXdpPdf(fs);
            shell.resolverPath = "C:\\Users\\John\\Desktop\\pdf";
            shell.ShellDocument.Add(new Paragraph("Your PDF reader cannot render XFA documents. Try Adobe Acrobat or Adobe Reader."));
            //template, datasets and config packets are always required!            
            
            {
                /*var fs = new FileStream("c:\\temp\\xfabuilder\\preamble", FileMode.Open);
                        
                StreamReader sr = new StreamReader(fs);
                elem.InnerXml = sr.ReadToEnd();
                */
                var xmlDoc = new XmlDocument();
                xmlDoc.Load("c:\\temp\\xfabuilder\\xdp");
                shell.SetPackage(xmlDoc);

            }
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.Load("c:\\temp\\xfabuilder\\config");
                shell.SetConfig(xmlDoc);
            }
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.Load("c:\\temp\\xfabuilder\\template");
                shell.SetTemplate(xmlDoc);
            }
            //shell.SetConfig(new FileStream("c:\\temp\\xfabuilder\\config", FileMode.Open));
           // shell.SetTemplate(new FileStream("c:\\temp\\xfabuilder\\template", FileMode.Open));
            //shell.SetLocaleSet(new FileStream("c:\\temp\\xfabuilder\\localeSet", FileMode.Open));
            //shell.SetXmpMeta(new FileStream("c:\\temp\\xfabuilder\\xmpmeta", FileMode.Open));
            //shell.SetXfdf(new FileStream("c:\\temp\\xfabuilder\\xfdf", FileMode.Open));
            //shell.SetForm(new FileStream("c:\\temp\\xfabuilder\\form", FileMode.Open));
            //shell.SetDatasets(new FileStream("c:\\temp\\xfabuilder\\datasets", FileMode.Open));
            /*{
                var xmlDoc = new XmlDocument();
                xmlDoc.Load("c:\\temp\\xfabuilder\\postamble");
            }*/
            //shell.SetPostamble(new FileStream("c:\\temp\\xfabuilder\\postamble", FileMode.Open));
            //shell.SetMetaData(new FileStream("c:\\temp\\xfabuilder\\metadata", FileMode.Open));
            shell.Close();
        }
    }
}