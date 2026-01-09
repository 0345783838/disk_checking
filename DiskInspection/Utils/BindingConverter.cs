using DiskInspection.Models;
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace DiskInspection.Utils
{
    public class FileNameConverterMain : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path)
            {
                var parentFolder = IO.GetParentFolderFromFilePath(path);
                var fileName = IO.GetFileName(path);
                return $"{parentFolder}/{fileName}"; // Trả về tên file từ đường dẫn
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value; // Không cần sử dụng ở đây, vì chỉ hiển thị tên file
        }
    }
    public class MyNullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null;   // Có ảnh -> true

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class StatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == 0) return "DarkGreen";
                else if (status == 1) return "Red";
                else if (status == 2) return "Black";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class MainStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == (int)(StatusState.OK)) return "CONNECTED";
                else if (status == (int)(StatusState.NG)) return "DISCONNECTED";
                return "UNKNOWN";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
    public class MainStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int status)
            {
                if (status == (int)(StatusState.OK)) return "DarkGreen";
                else if (status == (int)(StatusState.NG))  return "Red";
                return "Gray";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
