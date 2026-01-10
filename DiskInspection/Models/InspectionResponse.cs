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
    }
}
