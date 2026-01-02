using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Models
{
    public class DebugImageResponse
    {
        public bool Result { get; set; }
        public string DetectImg { get; set; }
        public string SegmentImg { get; set; }
        public string FinalImg { get; set; }
    }
}
