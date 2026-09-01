using System;
using System.Collections.Generic;
using LiteDB;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Represents a saved incident ticket template with pre-filled values
    /// </summary>
    public class IncidentTicketTemplate
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
        public string SubmittedBy { get; set; } = string.Empty;
        public string AffectedUserBuildingNumber { get; set; } = string.Empty;
        public string AffectedUserRoomNumber { get; set; } = string.Empty;
        public string BestContactMethod { get; set; } = string.Empty;
        public string AffectedUserPhoneNumber { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Subcategory { get; set; } = string.Empty;
        public string AffectedService { get; set; } = string.Empty;  // For "Affected Service" category - text input
        public string ShortDescription { get; set; } = string.Empty;
        public string ContactType { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public string Urgency { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AffectedSystemName { get; set; } = string.Empty;
        public string AffectedEndUser { get; set; } = string.Empty;  // Email or SAM - resolved to SNOW ID at submit time
        public string WorkNotes { get; set; } = string.Empty;  // Work notes to add to ticket

        // Resolved values (persisted so template loads don't re-trigger SNOW lookups)
        public string? ResolvedAffectedEndUserSysId { get; set; }
        public string? ResolvedAffectedEndUserDisplay { get; set; }

        // Post-load fields (set via CDP after page loads, to avoid ServiceNow script overrides)
        public string Location { get; set; } = string.Empty;
        public bool UseCdpForPostLoad { get; set; } = false;
    }

    /// <summary>
    /// Container for serializing templates to JSON
    /// </summary>
    public class IncidentTicketTemplatesFile
    {
        public List<IncidentTicketTemplate> Templates { get; set; } = new();
        public List<DropdownOption> CustomUsers { get; set; } = new();
        public List<DropdownOption> CustomLocations { get; set; } = new();
    }

    /// <summary>
    /// Represents a dropdown option with display text and URL value
    /// </summary>
    public class DropdownOption
    {
        [BsonId]
        public string UrlValue { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;

        public DropdownOption() { }

        public DropdownOption(string displayText, string urlValue)
        {
            DisplayText = displayText;
            UrlValue = urlValue;
        }

        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// Static class containing all ServiceNow field definitions and dropdown options
    /// </summary>
    public static class ServiceNowFields
    {
        public const string BaseUrl = "https://yourit.va.gov/incident.do?sys_id=-1&sysparm_query=";

        // URL field names
        public const string FieldCallerId = "caller_id";
        public const string FieldBuildingNumber = "u_affected_user_building_number";
        public const string FieldRoomNumber = "u_affected_user_room_number";
        public const string FieldContactMethod = "u_best_contact_method";
        public const string FieldPhoneNumber = "u_affected_user_phone_number";
        public const string FieldCategory = "category";
        public const string FieldSubcategory = "subcategory";
        public const string FieldAffectedService = "business_service";
        public const string FieldShortDescription = "short_description";
        public const string FieldContactType = "contact_type";
        public const string FieldImpact = "impact";
        public const string FieldUrgency = "urgency";
        public const string FieldDescription = "description";
        public const string FieldAffectedSystemName = "u_affected_system_name";
        public const string FieldAffectedEndUser = "u_affected_end_user";
        public const string FieldWorkNotes = "work_notes";

        // Best Contact Method options
        public static readonly List<DropdownOption> ContactMethodOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("Email", "Email"),
            new DropdownOption("Business Phone", "Business Phone"),
            new DropdownOption("Mobile Phone", "Mobile%20Phone"),
            new DropdownOption("Other", "Other"),
            new DropdownOption("Instant Messaging", "Instant Messaging")
        };

        // Category options
        public static readonly List<DropdownOption> CategoryOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("Facility", "Facility"),
            new DropdownOption("Hardware", "Hardware"),
            new DropdownOption("Security", "Security"),
            new DropdownOption("Service", "Service"),
            new DropdownOption("Software", "Software"),
            new DropdownOption("Affected Service", "enterprise_application"),
            new DropdownOption("Password Reset", "Application"),
            new DropdownOption("Web Applications", "web_application")
        };

        // Subcategory options by category
        public static readonly Dictionary<string, List<DropdownOption>> SubcategoryOptions = new()
        {
            ["Facility"] = new List<DropdownOption>
            {
                new DropdownOption("", ""),
                new DropdownOption("Structure", "Structure Repair")
            },
            ["Hardware"] = new List<DropdownOption>
            {
                new DropdownOption("", ""),
                new DropdownOption("Desktop", "Desktop Repair"),
                new DropdownOption("Laptop", "Laptop Repair"),
                new DropdownOption("Mobile", "Mobile"),
                new DropdownOption("Network", "Network Repair"),
                new DropdownOption("Peripheral", "Peripheral Repair"),
                new DropdownOption("Printer", "Printer Repair"),
                new DropdownOption("Server", "Server Repair"),
                new DropdownOption("Storage", "Storage Repair"),
                new DropdownOption("Telecom", "Telecom Repair"),
                new DropdownOption("Video Teleconferencing (VTC)", "Video Teleconferencing (VTC)")
            },
            ["Security"] = new List<DropdownOption>
            {
                new DropdownOption("", ""),
                new DropdownOption("Lost/Stolen PIV Card", "Lost/Stolen PIV Card"),
                new DropdownOption("Scan Investigate", "Scan Investigate"),
                new DropdownOption("Scan Remediate", "Scan Remediate"),
                new DropdownOption("Unauthorized Software Remediate", "Scan Unauthorized Software"),
                new DropdownOption("Local Investigate", "Local Investigate"),
                new DropdownOption("Virus Investigate", "Virus Investigate"),
                new DropdownOption("Virus Remediate", "Virus Remediate")
            },
            ["Service"] = new List<DropdownOption>
            {
                new DropdownOption("", ""),
                new DropdownOption("Access", "Access"),
                new DropdownOption("EHRM Cerner", "Cerner"),
                new DropdownOption("VA Enterprise Cloud - VAEC", "vaec"),
                new DropdownOption("Account Provisioning/Deprovisioning System (APDS)", "va on-offboarding service")
            },
            ["Software"] = new List<DropdownOption>
            {
                new DropdownOption("", ""),
                new DropdownOption("ActiveDirectory", "ActiveDirectory Repair"),
                new DropdownOption("Backup", "backup_software"),
                new DropdownOption("BDN-Password", "BDNPassword_Software"),
                new DropdownOption("Database", "Database Repair"),
                new DropdownOption("Desktop Application", "Application Repair"),
                new DropdownOption("Email", "Email"),
                new DropdownOption("Server", "Server Repair"),
                new DropdownOption("Web", "Web Repair")
            },
            ["enterprise_application"] = new List<DropdownOption>(), // Uses text input for Affected Service
            ["Application"] = new List<DropdownOption>
            {
                new DropdownOption("", ""),
                new DropdownOption("Password Reset", "Password Reset")
            },
            ["web_application"] = new List<DropdownOption>
            {
                new DropdownOption("", ""),
                new DropdownOption("Impacted Service", "impacted_service"),
                new DropdownOption("Digital Transformation Server", "dtc"),
                new DropdownOption("Financial Service Center - FSC", "fsc"),
                new DropdownOption("Life Insurance Policy Admin Sys - LIPAS", "lipas"),
                new DropdownOption("Pharmacy Benefits Management (PBM) - Consolidated Mail Outpatient Pharmacy (CMOP)", "consolidated_mail_out"),
                new DropdownOption("Provider Profile Management System - PPMS", "ppms"),
                new DropdownOption("Readiness and Employment System (RES)", "res"),
                new DropdownOption("Veteran Appeals System – Caseflow", "caseflow"),
                new DropdownOption("Veterans Benefit Management System - VBMS", "vbms"),
                new DropdownOption("VRM Dynamics CRM", "vrm_dynamics_crm")
            }
        };

        // Contact Type options
        public static readonly List<DropdownOption> ContactTypeOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("Email", "email"),
            new DropdownOption("Phone", "phone"),
            new DropdownOption("ITIL Self-Service", "ITIL Self-Service"),
            new DropdownOption("Walk-in", "walk-in"),
            new DropdownOption("Chat", "Chat"),
            new DropdownOption("Option 9", "Option 9"),
            new DropdownOption("SMS", "sms"),
            new DropdownOption("MS Teams Chat", "MS Teams Chat")
        };

        // Impact options
        public static readonly List<DropdownOption> ImpactOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("1 - Critical - Impacts National", "1"),
            new DropdownOption("2 - High - Impacts Regional", "2"),
            new DropdownOption("3 - Medium - Impacts Office / Floor", "3"),
            new DropdownOption("4 - Low - Impact is one or more, but not all users", "4")
        };

        // Urgency options
        public static readonly List<DropdownOption> UrgencyOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("1(Critical) System outage (Application or Service)", "1"),
            new DropdownOption("2(High) Customer work stoppage", "2"),
            new DropdownOption("3(Medium) Resolution scheduled when resources are available. Work Around is Available.", "3"),
            new DropdownOption("4(Low) Convenience vs. function - All other issues", "4")
        };

        // Predefined team members for Submitted By (Name -> sys_id)
        public static readonly List<DropdownOption> SubmittedByOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("Bill", "459cbbbcdbe9d700aec6740d0f9619c7"),
            new DropdownOption("Dom", "66302591977836901b7419900153afb9"),
            new DropdownOption("Larry", "e3f36261dbff03002d2a70c08c961958"),
            new DropdownOption("Megan", "37846625dbff03002d2a70c08c961924"),
            new DropdownOption("Michael B", "9a406991977836901b7419900153af0e"),
            new DropdownOption("Michael S", "af53a9be87f3229835b6a6450cbb35b1"),
            new DropdownOption("Mo", "87585b42db7aff44c982f4d40f9619d5"),
            new DropdownOption("Shawn", "d594e625dbff03002d2a70c08c96197b"),
            new DropdownOption("Tim", "fc1f4dd71bd71050f8414262f54bcbfc"),
            new DropdownOption("Thad", "5d6d3f70db6dd700aec6740d0f9619f9")
        };

        // Predefined locations (Display Name -> sys_id)
        public static readonly List<DropdownOption> LocationOptions = new()
        {
            new DropdownOption("", ""),
            new DropdownOption("Anc VA Med Center", "1e9a884e1bea94d412979796bc4bcb19"),
            new DropdownOption("Anc VA Benefit Office", "9e9a884e1bea94d412979796bc4bcb18"),
            new DropdownOption("Anc Vet Center", "81aa408e1bea94d412979796bc4bcb00"),
            new DropdownOption("Anc", "317c7be1dbe4d700ceae3a8c7c961918")
        };

        /// <summary>
        /// Gets subcategory options for a given category URL value
        /// </summary>
        public static List<DropdownOption> GetSubcategoryOptions(string categoryUrlValue)
        {
            if (string.IsNullOrEmpty(categoryUrlValue))
                return new List<DropdownOption> { new DropdownOption("", "") };

            // Find the display text for this URL value
            var categoryDisplay = CategoryOptions.Find(c => c.UrlValue == categoryUrlValue)?.DisplayText;
            
            // Try to find subcategories by display text first, then by URL value
            if (!string.IsNullOrEmpty(categoryDisplay) && SubcategoryOptions.TryGetValue(categoryDisplay, out var optionsByDisplay))
                return optionsByDisplay;
            
            if (SubcategoryOptions.TryGetValue(categoryUrlValue, out var optionsByUrl))
                return optionsByUrl;

            return new List<DropdownOption> { new DropdownOption("", "") };
        }

        /// <summary>
        /// Checks if a category uses a text input for subcategory instead of dropdown
        /// </summary>
        public static bool CategoryUsesTextSubcategory(string categoryUrlValue)
        {
            return categoryUrlValue == "enterprise_application";
        }
    }

    /// <summary>
    /// Browser options for opening tickets
    /// </summary>
    public enum TicketBrowser
    {
        Edge,
        Chrome
    }
}
