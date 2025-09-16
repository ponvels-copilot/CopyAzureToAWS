using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AzureToAWS.Data.Models;

[Table("call_details", Schema = "dbo")]
public class TableCallDetails
{
    [Key]
    [Column("calldetailid")]
    public long CallDetailID { get; set; }

    [Column("calldate")]
    public DateTime CallDate { get; set; }

    [Column("programcode")]
    public string? ProgramCode { get; set; }
}