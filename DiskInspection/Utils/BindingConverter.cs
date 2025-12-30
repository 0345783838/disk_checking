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
}
