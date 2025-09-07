using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CopyAzureToAWS.Data.Models;

[Table("call_details", Schema = "dbo")]
public class TableCallDetails
{
    [Key]
    [Column("calldetailid")]
    public long CallDetailID { get; set; }

    [Column("programcode")]
    public string? ProgramCode { get; set; }
}