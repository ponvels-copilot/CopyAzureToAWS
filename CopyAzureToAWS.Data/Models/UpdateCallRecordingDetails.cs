using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CopyAzureToAWS.Data.Models
{
    public class UpdateCallRecordingDetails
    {
        public long CallDetailID { get; set; }
        public string? AudioFile { get; set; }
        public string? AudioFileLocation { get; set; }
        public string? S3Md5 { get; set; }
        public long S3SizeBytes { get; set; }
        public string? Status { get; set; }
        public string? ErrorDescription { get; set; }
        public string RequestId { get; set; } = string.Empty;
    }
}
