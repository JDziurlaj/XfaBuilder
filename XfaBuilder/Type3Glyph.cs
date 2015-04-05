using System;
using iTextSharp.text;
using iTextSharp.text.error_messages;

namespace cute{

    /**
    * The content where Type3 glyphs are written to.
    */
    public sealed class Type3Glyph : iTextSharp.text.pdf.PdfContentByte {

        private iTextSharp.text.pdf.PageResources pageResources;
        private bool colorized;
        
        private Type3Glyph() : base(null) {
        }
        
        internal Type3Glyph(iTextSharp.text.pdf.PdfWriter writer, iTextSharp.text.pdf.PageResources pageResources, float wx, float llx, float lly, float urx, float ury, bool colorized) : base(writer) {
            this.pageResources = pageResources;
            this.colorized = colorized;
            if (colorized) {
                content.Append(wx).Append(" 0 d0\n");
            }
            else {
                content.Append(wx).Append(" 0 ").Append(llx).Append(' ').Append(lly).Append(' ').Append(urx).Append(' ').Append(ury).Append(" d1\n");
            }
        }
        
        internal iTextSharp.text.pdf.PageResources PageResources {
            get {
                return pageResources;
            }
        }
        
        public override void AddImage(Image image, float a, float b, float c, float d, float e, float f, bool inlineImage) {
            if (!colorized && (!image.IsMask() || !(image.Bpc == 1 || image.Bpc > 0xff)))
                throw new DocumentException(MessageLocalization.GetComposedMessage("not.colorized.typed3.fonts.only.accept.mask.images"));
            base.AddImage(image, a, b, c, d, e, f, inlineImage);
        }

        public iTextSharp.text.pdf.PdfContentByte GetDuplicate() {
            Type3Glyph dup = new Type3Glyph();
            dup.writer = writer;
            dup.pdf = pdf;
            dup.pageResources = pageResources;
            dup.colorized = colorized;
            return dup;
        }    
    }
}
