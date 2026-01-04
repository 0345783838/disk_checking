using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Utils
{
    public class Converter
    {

        public static Bitmap Base64ToBitmap(string base64)
        {
            // Nếu base64 có prefix kiểu: data:image/png;base64,...
            if (base64.Contains(","))
                base64 = base64.Substring(base64.IndexOf(",") + 1);

            byte[] imageBytes = Convert.FromBase64String(base64);

            using (var ms = new MemoryStream(imageBytes))
            {
                return new Bitmap(ms);
            }
        }
    }
}
