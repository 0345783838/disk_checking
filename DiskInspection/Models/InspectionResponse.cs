using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Models
{
    
    public class InspectionResponse
    {
        public bool Result { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorDesc { get; set; }
        public string ResImg { get; set; }
        public double MinDiskDistance { get; set; }
        public double MaxDiskDistance { get; set; }
        public string CropBox { get; set; }
        public string UvBox1 { get; set; }
        public string UvBox2 { get; set; }
        public string Mid1 { get; set; }
        public string Mid2 { get; set; }
    }
}
