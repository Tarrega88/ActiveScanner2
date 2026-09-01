using System.Collections.Generic;

namespace ActiveScanner.Models
{
    /// <summary>
    /// Defines which columns/attributes to query and display
    /// </summary>
    public class ColumnSettings
    {
        // Common properties
        public bool Name { get; set; } = true;
        public bool Description { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        public bool LastLogon { get; set; } = true;
        public bool WhenCreated { get; set; } = false;
        public bool WhenChanged { get; set; } = false;
        public bool ManagedBy { get; set; } = false;
        public bool DistinguishedName { get; set; } = false;
        public bool SourcePath { get; set; } = true;

        // Computer-specific properties
        public bool DnsHostName { get; set; } = true;
        public bool OperatingSystem { get; set; } = false;
        public bool OperatingSystemVersion { get; set; } = false;
        public bool Location { get; set; } = true;

        // User-specific properties
        public bool SamAccountName { get; set; } = true;
        public bool UserPrincipalName { get; set; } = false;
        public bool DisplayName { get; set; } = true;
        public bool GivenName { get; set; } = false;
        public bool Surname { get; set; } = false;
        public bool Mail { get; set; } = true;
        public bool Title { get; set; } = true;
        public bool Department { get; set; } = true;
        public bool Company { get; set; } = false;
        public bool Manager { get; set; } = true;
        public bool TelephoneNumber { get; set; } = false;
        public bool Mobile { get; set; } = false;
        public bool Office { get; set; } = false;
        public bool PasswordLastSet { get; set; } = false;
        public bool AccountExpires { get; set; } = false;

        // Printer-specific properties
        public bool PrinterName { get; set; } = true;
        public bool ServerName { get; set; } = true;
        public bool ShareName { get; set; } = true;
        public bool PortName { get; set; } = false;
        public bool DriverName { get; set; } = true;
        public bool UNCName { get; set; } = true;
        public bool PrinterPriority { get; set; } = false;

        /// <summary>
        /// Gets the LDAP property names to load for computers
        /// </summary>
        public List<string> GetComputerLdapProperties()
        {
            // Always include objectGUID for caching/identification purposes
            var props = new List<string> { "name", "objectClass", "objectGUID" };

            if (DnsHostName) props.Add("dNSHostName");
            if (Description) props.Add("description");
            if (OperatingSystem) props.Add("operatingSystem");
            if (OperatingSystemVersion) props.Add("operatingSystemVersion");
            if (IsEnabled) props.Add("userAccountControl");
            if (LastLogon) props.Add("lastLogonTimestamp");
            if (WhenCreated) props.Add("whenCreated");
            if (WhenChanged) props.Add("whenChanged");
            if (Location) props.Add("location");
            if (ManagedBy) props.Add("managedBy");
            if (DistinguishedName || SourcePath) props.Add("distinguishedName");

            return props;
        }

        /// <summary>
        /// Gets the LDAP property names to load for users
        /// </summary>
        public List<string> GetUserLdapProperties()
        {
            // Always include objectGUID for caching/identification purposes
            var props = new List<string> { "name", "objectClass", "objectGUID" };

            if (SamAccountName) props.Add("sAMAccountName");
            if (UserPrincipalName) props.Add("userPrincipalName");
            if (DisplayName) props.Add("displayName");
            if (GivenName) props.Add("givenName");
            if (Surname) props.Add("sn");
            if (Description) props.Add("description");
            if (Mail) props.Add("mail");
            if (Title) props.Add("title");
            if (Department) props.Add("department");
            if (Company) props.Add("company");
            if (Manager) props.Add("manager");
            if (TelephoneNumber) props.Add("telephoneNumber");
            if (Mobile) props.Add("mobile");
            if (Office) props.Add("physicalDeliveryOfficeName");
            if (IsEnabled) props.Add("userAccountControl");
            if (LastLogon) props.Add("lastLogonTimestamp");
            if (PasswordLastSet) props.Add("pwdLastSet");
            if (AccountExpires) props.Add("accountExpires");
            if (WhenCreated) props.Add("whenCreated");
            if (WhenChanged) props.Add("whenChanged");
            if (ManagedBy) props.Add("managedBy");
            if (DistinguishedName || SourcePath) props.Add("distinguishedName");

            return props;
        }

        /// <summary>
        /// Gets the LDAP property names to load for printers
        /// </summary>
        public List<string> GetPrinterLdapProperties()
        {
            // Always include objectGUID for caching/identification purposes
            // Always include portName for IP address resolution
            var props = new List<string> { "name", "objectClass", "objectCategory", "objectGUID", "portName" };

            if (PrinterName) props.Add("printerName");
            if (Description) props.Add("description");
            if (ServerName) props.Add("serverName");
            if (ShareName) props.Add("printShareName");
            // portName is now always included above for IP resolution
            if (DriverName) props.Add("driverName");
            if (UNCName) props.Add("uNCName");
            if (Location) props.Add("location");
            if (PrinterPriority) props.Add("priority");
            if (WhenCreated) props.Add("whenCreated");
            if (WhenChanged) props.Add("whenChanged");
            if (DistinguishedName || SourcePath) props.Add("distinguishedName");

            return props;
        }

        /// <summary>
        /// Gets the LDAP property names to load based on settings (legacy - for computers)
        /// </summary>
        public List<string> GetLdapProperties() => GetComputerLdapProperties();
    }
}
