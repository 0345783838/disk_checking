using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace DiskInspection.Models
{
    enum StatusState
    {
        Ok = 0,
        Ng = 1,
        Inspecting = 3,
        Unknown = 2,
        Stopped = 4
    }
    enum TriggerState
    {
        Ok = 1,
        Error = -1
    }
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
    public enum ThumbStatus
    {
        Origin,
        Ok,
        Ng
    }
}
