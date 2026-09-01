using System;
using LiteDB;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Mode for handling multiple selected items.
    /// </summary>
    public enum EmailRecipientMode
    {
        /// <summary>One email per selected item (placeholders evaluated per item).</summary>
        PerItem = 0,
        /// <summary>One combined email; To/Cc/Bcc placeholders aggregated across all items.</summary>
        Combined = 1
    }

    /// <summary>
    /// Represents a saved Outlook email template with pre-filled values.
    /// All text fields support {{ColumnName}} syntax for dynamic replacement
    /// against the selected AD object (User / Computer / Printer).
    /// </summary>
    public class EmailTemplate
    {
        [BsonId]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

        // Address fields - semicolon separated, support {{Placeholder}}
        public string To { get; set; } = string.Empty;
        public string Cc { get; set; } = string.Empty;
        public string Bcc { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        /// <summary>Email body (plain text or HTML based on <see cref="IsHtml"/>).</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>If true, body is treated as HTML; otherwise plain text.</summary>
        public bool IsHtml { get; set; } = false;

        /// <summary>How to handle multiple selected items.</summary>
        public EmailRecipientMode RecipientMode { get; set; } = EmailRecipientMode.PerItem;
    }
}
