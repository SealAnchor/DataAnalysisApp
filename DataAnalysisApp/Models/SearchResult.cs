using System.Collections.Generic;
using System.Data;

namespace DataAnalysisApp.Models
{
    public class SearchResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string FolderPath { get; set; }
        public List<string> MatchedHeaders { get; set; }
        public DataTable FilteredData { get; set; }
        public string HeaderSignature { get; set; }

        public SearchResult()
        {
            MatchedHeaders = new List<string>();
        }
    }
}
