using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Domain
{
    public class InspectSummary
    {
        public bool Cam1Ok { get; private set; }
        public bool Cam2Ok { get; private set; }


        public InspectSummary(bool cam1Ok, bool cam2Ok)
        {
            Cam1Ok = cam1Ok;
            Cam2Ok = cam2Ok;
        }
    }
}
