using System.Collections.Generic;
using System.Data;

namespace DataAnalysisApp.Models
{
    public class CsvFileInfo
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string FolderPath { get; set; }
        public List<string> Headers { get; set; }
        public DataTable Data { get; set; }
        public bool IsDataLoaded { get; set; }

        public CsvFileInfo()
        {
            Headers = new List<string>();
            IsDataLoaded = false;
        }
    }
}
