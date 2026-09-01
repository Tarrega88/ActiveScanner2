using System;
using System.Collections.Generic;
using LiteDB;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a saved SCTASK ticket template with pre-filled values
    /// </summary>
    public class SctaskTicketTemplate
    {
        /// <summary>
        /// Unique identifier for this template
        /// </summary>
        [BsonId]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// User-friendly name for this template
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// When this template was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When this template was last modified
        /// </summary>
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        // Field values - all support {{ColumnName}} syntax for dynamic replacement
        public string RequestItem { get; set; } = string.Empty;  // RITM number - validated to get sys_id
        public string RequestedFor { get; set; } = string.Empty;  // Email or SAM - resolved to SNOW ID at submit time
        public string AssignmentGroup { get; set; } = string.Empty;  // Group sys_id or name (validated)
        public string AssignmentGroupDropdownValue { get; set; } = string.Empty;  // Quick-select dropdown UrlValue
        public string AssignedTo { get; set; } = string.Empty;  // Email or SAM - resolved to SNOW ID at submit time
        public string ShortDescription { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string WorkNotes { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;  // Dropdown value (1-5)
        public string State { get; set; } = string.Empty;  // Dropdown value (-5, 1, 2, 3, 4, 7)

        // Resolved values (persisted so template loads don't re-trigger SNOW lookups)
        public string? ResolvedRequestedForSysId { get; set; }
        public string? ResolvedRequestedForDisplay { get; set; }
        public string? ResolvedAssignmentGroupSysId { get; set; }
        public string? ResolvedAssignmentGroupDisplay { get; set; }
        public string? ResolvedAssignedToSysId { get; set; }
        public string? ResolvedAssignedToDisplay { get; set; }
    }

    /// <summary>
    /// Static class containing all ServiceNow SCTASK field definitions and dropdown options
    /// </summary>
    public static class SctaskFields
    {
        public const string BaseUrl = "https://yourit.va.gov/sc_task.do?sys_id=-1&sysparm_query=";

        // URL field names
        public const string FieldRequestItem = "request_item";
        public const string FieldRequestedFor = "requested_for";
        public const string FieldAssignmentGroup = "assignment_group";
        public const string FieldAssignedTo = "assigned_to";
        public const string FieldShortDescription = "short_description";
        public const string FieldDescription = "description";
        public const string FieldWorkNotes = "work_notes";
        public const string FieldPriority = "priority";
        public const string FieldState = "state";

        // Priority options (same as Incident)
        public static readonly List<DropdownOption> PriorityOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("1 - Critical", "1"),
            new DropdownOption("2 - High", "2"),
            new DropdownOption("3 - Moderate", "3"),
            new DropdownOption("4 - Low", "4")
        };

        // State options for SCTASK (from ServiceNow HTML)
        public static readonly List<DropdownOption> StateOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("Pending", "-5"),
            new DropdownOption("Open", "1"),
            new DropdownOption("Work in Progress", "2"),
            new DropdownOption("Closed Complete", "3"),
            new DropdownOption("Closed Incomplete", "4"),
            new DropdownOption("Closed Skipped", "7")
        };

        // Predefined assignment groups (quick-select dropdown)
        public static readonly List<DropdownOption> AssignmentGroupOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("PA ANC OPS", "5c5bb5f1dbf28b007ed130ca7c961971")
        };
    }
}
