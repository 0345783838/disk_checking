using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiskInspection.Models
{
    public class EnvironmentConfig
    {
        public float DetectThreshold { get; set; }
        public float DetectIou { get; set; }
        public float SegmentThreshold { get; set; }

        public float CaliperMinEdgeDistance { get; set; }
        public float CaliperMaxEdgeDistance { get; set; }
        public float CaliperLengthRate { get; set; }
        public List<int> CaliperThicknessList { get; set; }

        public int DiskNumber { get; set; }
        public float DiskMaxDistance { get; set; }
        public float DiskMinDistance { get; set; }
        public float DiskMinArea { get; set; }

        public EnvironmentConfig() { }
        public EnvironmentConfig(float detectThreshold, float detectIoU, float segmentThreshold, 
            float caliperMinEdgeDistance, float caliperMaxEdgeDistance, float caliperLengthRate,
            List<int> caliperThicknessList, int diskNumber, float diskMaxDistance, float diskMinDistance, float diskMinArea)
        {
            DetectThreshold = detectThreshold;
            DetectIou = detectIoU;
            SegmentThreshold = segmentThreshold;
            CaliperMinEdgeDistance = caliperMinEdgeDistance;
            CaliperMaxEdgeDistance = caliperMaxEdgeDistance;
            CaliperLengthRate = caliperLengthRate;
            CaliperThicknessList = caliperThicknessList;
            DiskNumber = diskNumber;
            DiskMaxDistance = diskMaxDistance;
            DiskMinDistance = diskMinDistance;
            DiskMinArea = diskMinArea;
        }
    }
}
