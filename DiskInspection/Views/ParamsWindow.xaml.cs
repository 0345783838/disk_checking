using DiskInspection.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DiskInspection.Views
{
    /// <summary>
    /// Interaction logic for ParamsWindow.xaml
    /// </summary>
    public partial class ParamsWindow : Window
    {
        EnvironmentConfig _config;
        DebugWindow _debugWindow;
        public ParamsWindow(DebugWindow debugWindow, EnvironmentConfig config)
        {
            InitializeComponent();
            _config = config;
            _debugWindow = debugWindow;
            UpdateConfig(_config);
        }

        private void UpdateConfig(EnvironmentConfig config)
        {
            tbDetectThreshold.Text = config.DetectThreshold.ToString();
            tbDetectIoU.Text = config.DetectIou.ToString();
            tbSegmentThreshold.Text = config.SegmentThreshold.ToString();
            tbMinEdgeDistance.Text = config.CaliperMinEdgeDistance.ToString();
            tbMaxEdgeDistance.Text = config.CaliperMaxEdgeDistance.ToString();
            tbLengthRate.Text = config.CaliperLengthRate.ToString();
            tbThicknessList.Text = string.Join(",", config.CaliperThicknessList);
            tbTotalDisks.Text = config.DiskNumber.ToString();
            tbDiskMaxDistance.Text = config.DiskMaxDistance.ToString();
            tbDiskMinDistance.Text = config.DiskMinDistance.ToString();
            tbDiskMinArea.Text = config.DiskMinArea.ToString();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {

        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // Check if params changed?
            var thicknessList = tbThicknessList.Text.Split(',').Select(x => int.Parse(x)).ToList();
            if (_config.DetectThreshold != float.Parse(tbDetectThreshold.Text) || _config.DetectIou != float.Parse(tbDetectIoU.Text) || _config.SegmentThreshold != float.Parse(tbSegmentThreshold.Text)
                || _config.CaliperMinEdgeDistance != float.Parse(tbMinEdgeDistance.Text) || _config.CaliperMaxEdgeDistance != float.Parse(tbMaxEdgeDistance.Text) || !_config.CaliperThicknessList.SequenceEqual(thicknessList)
                || _config.CaliperLengthRate != float.Parse(tbLengthRate.Text)|| _config.DiskNumber != int.Parse(tbTotalDisks.Text) || _config.DiskMaxDistance != float.Parse(tbDiskMaxDistance.Text)
                || _config.DiskMinDistance != float.Parse(tbDiskMinDistance.Text) || _config.DiskMinArea != float.Parse(tbDiskMinArea.Text))
            {
                var newConfig = new EnvironmentConfig(float.Parse(tbDetectThreshold.Text), float.Parse(tbDetectIoU.Text), float.Parse(tbSegmentThreshold.Text), float.Parse(tbMinEdgeDistance.Text),
                    float.Parse(tbMaxEdgeDistance.Text), float.Parse(tbLengthRate.Text), thicknessList, int.Parse(tbTotalDisks.Text), float.Parse(tbDiskMaxDistance.Text), 
                    float.Parse(tbDiskMinDistance.Text), float.Parse(tbDiskMinArea.Text));

                _debugWindow.UpdateConfig(newConfig);
            }
        }
    }
}
