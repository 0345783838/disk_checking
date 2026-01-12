using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Domain
{
    public enum InspectState
    {
        Idle,
        WaitTrigger,
        InspectCam1,
        InspectCam2,
        Report
    }
}