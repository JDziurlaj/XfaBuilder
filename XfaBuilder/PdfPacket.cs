using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace XfaBuilder
{
    class PdfPacket
    {
       
        private string _uri;
        private string _pdf;

        public Boolean KeepOnDisk { get; set; }
        public string Href { get { return _uri; } set { _uri = value; } }

        public XmlDocument Packet
        {
            get
            {
                XmlDocument _packet = new XmlDocument();
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(_packet.NameTable);                               
                nsmgr.AddNamespace("pdf", "http://ns.adobe.com/xdp/pdf/");
                if (_pdf != null)
                {
                    var document = _packet.CreateElement("document");
                    _packet.AppendChild(document);

                    var chunk = _packet.CreateElement("chunk");
                    chunk.InnerText = _pdf;
                    document.AppendChild(chunk);

                }

                return _packet;
            }
        }

        private PdfPacket()
        {
            KeepOnDisk = true;
        }

        public PdfPacket(string uri) : this()
        {           
            this._uri = uri;
        }

        public PdfPacket(byte[] pdf) : this()
        {
            this._pdf = Convert.ToBase64String(pdf);
        }

      /*  public PdfPacket(string encodedPdf) : this()
        {
            this._pdf = encodedPdf;
        }
        */
    }
}
