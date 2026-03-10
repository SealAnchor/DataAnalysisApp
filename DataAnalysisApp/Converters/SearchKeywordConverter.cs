using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DataAnalysisApp.Converters
{
    public class SearchKeywordConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return Brushes.Transparent;

            var cellValue = values[0]?.ToString() ?? string.Empty;
            var searchKeyword = values[1]?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(searchKeyword))
                return Brushes.Transparent;

            if (cellValue.IndexOf(searchKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return Brushes.Yellow;

            return Brushes.Transparent;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
