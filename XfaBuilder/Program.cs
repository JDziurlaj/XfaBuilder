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
 * Program under terms of license located in license.txt
 */
namespace XfaPdfBuilder
{    
    public class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("XfaBuilder");

          /*  {
                var img = new FileStream("C:\\temp\\pdf\\7.0\\empty static.pdf", FileMode.Open);
                var reader = new BinaryReader(img);
                var pdfpack = new PdfPacket(reader.ReadBytes((int)img.Length));
                Console.WriteLine(pdfpack.Packet.InnerXml);
                Console.ReadKey();
            }*/


            var fs = new FileStream("C:\\temp\\pdf\\emi.pdf", FileMode.Create);
            var shell = new ShellXdpPdf(fs);
            //set location where external references (in XDP packets) can be found
            shell.resolverPath = "C:\\temp\\pdf";

            var currentMethod = LayoutStyle.Stream;
            shell.style = LayoutStyle.Array;
                                    
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