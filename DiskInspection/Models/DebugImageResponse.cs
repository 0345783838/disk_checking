using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Models
{
    public class DebugImageResponse
    {
        public int Result { get; set; }
        public string DetectImg { get; set; }
        public string SegmentImg { get; set; }
        public string FinalImg { get; set; }
        public string CropBox { get; set; }
        public string UvBox1 { get; set; }
        public string UvBox2 { get; set; }
        public string Mid1 { get; set; }
        public string Mid2 { get; set; }
    }
}
