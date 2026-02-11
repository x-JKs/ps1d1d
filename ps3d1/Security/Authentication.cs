using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace ps3d1.Security
{
    /// <summary>
    /// License information structure
    /// </summary>
    public class LicenseInfo
    {
        public string Username { get; set; } = "";
        public string LicenseKey { get; set; } = "";
        public string HWID { get; set; } = "";
        public string LicenseType { get; set; } = "";
        public bool IsPermanent { get; set; } = false;
        public int DaysRemaining { get; set; } = 0;
        public bool IsValid { get; set; } = false;
    }

    /// <summary>
    /// Authentication system - C# port of Loader_V1 authentication
    /// </summary>
    public static class Authentication
    {
        public static event Action AuthStateChanged;
        // API Configuration - same as Loader_V1
        private const string API_SERVER = "https://lcbot-2sdb.onrender.com";
        private const string API_PATH_CHECK = "/api/licenses/";
        private const string ENCRYPTION_KEY = "SECURE_KEY_CHANGE_THIS_2025";
        private const string LICENSE_FILE = "license.dat";

        // State
        private static bool _isAuthenticated = false;
        private static LicenseInfo _currentLicense = new LicenseInfo();
        private static bool _licenseTerminated = false;
        private static CancellationTokenSource _backgroundCts = null;
        private static Task _backgroundTask = null;
        private static readonly object _lockObject = new object();

        /// <summary>
        /// Initialize authentication system
        /// </summary>
        public static bool Initialize()
        {
            LoadCredentials();
            return true;
        }

        /// <summary>
        /// Shutdown authentication system
        /// </summary>
        public static void Shutdown()
        {
            StopBackgroundVerification();
        }

        /// <summary>
        /// Start background license verification (checks every 5 minutes)
        /// </summary>
        public static void StartBackgroundVerification()
        {
            if (_backgroundTask != null && !_backgroundTask.IsCompleted)
                return;

            _backgroundCts = new CancellationTokenSource();
            _backgroundTask = Task.Run(async () =>
            {
                while (!_backgroundCts.Token.IsCancellationRequested)
                {
                    // Wait 5 minutes
                    try
                    {
                        await Task.Delay(300000, _backgroundCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    if (_backgroundCts.Token.IsCancellationRequested)
                        break;

                    // Verify license
                    lock (_lockObject)
                    {
                        if (_isAuthenticated && !string.IsNullOrEmpty(_currentLicense.LicenseKey))
                        {
                            string hwid = GenerateHWID();
                            bool isValid = VerifyLicenseOnline(_currentLicense.Username, _currentLicense.LicenseKey, hwid);

                            if (!isValid)
                            {
                                _licenseTerminated = true;
                                _isAuthenticated = false;

                                try { File.Delete(LICENSE_FILE); } catch { }
                                NotifyAuthStateChanged();
                            }
                        }
                    }
                }
            }, _backgroundCts.Token);
        }

        /// <summary>
        /// Stop background verification
        /// </summary>
        public static void StopBackgroundVerification()
        {
            if (_backgroundCts != null)
            {
                _backgroundCts.Cancel();
                try
                {
                    _backgroundTask?.Wait(1000);
                }
                catch { }
                _backgroundCts = null;
            }
        }

        /// <summary>
        /// Check if license was terminated
        /// </summary>
        public static bool WasLicenseTerminated()
        {
            return _licenseTerminated;
        }

        /// <summary>
        /// Validate license key format - matches Loader_V1 exactly
        /// Expected format: XXXX-XXXX-XXXX-XXXX (16 chars + 3 dashes = 19 total)
        /// </summary>
        public static bool ValidateLicenseFormat(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            // Expected format: XXXX-XXXX-XXXX-XXXX (19 total)
            if (key.Length != 19)
                return false;

            // Check dash positions
            if (key[4] != '-' || key[9] != '-' || key[14] != '-')
                return false;

            // Check that other characters are alphanumeric
            for (int i = 0; i < key.Length; i++)
            {
                if (i == 4 || i == 9 || i == 14) continue;
                if (!char.IsLetterOrDigit(key[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Generate Hardware ID from system information - matches Loader_V1 exactly
        /// Uses: CPU ID + MAC address + Volume serial
        /// </summary>
        public static string GenerateHWID()
        {
            StringBuilder sb = new StringBuilder();

            try
            {
                // Get CPU ID (similar to __cpuid in C++)
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        sb.Append(mo["ProcessorId"]?.ToString() ?? "");
                        break;
                    }
                }
            }
            catch { sb.Append("CPU_UNKNOWN"); }

            try
            {
                // Get MAC address (first network adapter)
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string mac = mo["MACAddress"]?.ToString();
                        if (!string.IsNullOrEmpty(mac))
                        {
                            // Remove colons to match Loader_V1 format
                            sb.Append(mac.Replace(":", "").Replace("-", ""));
                            break;
                        }
                    }
                }
            }
            catch { sb.Append("MAC_UNKNOWN"); }

            try
            {
                // Get volume serial number from C: drive
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID = 'C:'"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        sb.Append(mo["VolumeSerialNumber"]?.ToString() ?? "");
                        break;
                    }
                }
            }
            catch { sb.Append("VOL_UNKNOWN"); }

            // Hash the combined info (same as Loader_V1)
            return Encryption.GenerateHash(sb.ToString());
        }

        /// <summary>
        /// Verify HWID matches current system
        /// </summary>
        private static bool VerifyHWID(string storedHWID)
        {
            string currentHWID = GenerateHWID();
            return currentHWID == storedHWID;
        }

        /// <summary>
        /// Verify license against online API - matches Loader_V1 exactly
        /// </summary>
        private static bool VerifyLicenseOnline(string username, string key, string hwid)
        {
            try
            {
                // Build API path: /api/licenses/XXXX-XXXX-XXXX-XXXX
                string url = API_SERVER + API_PATH_CHECK + Uri.EscapeDataString(key);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.DefaultRequestHeaders.Add("User-Agent", "License Checker/1.0");
                    
                    var response = client.GetStringAsync(url).Result;

                    if (string.IsNullOrEmpty(response))
                        return false;

                    // Check if request was successful (Loader_V1 checks for "success":true)
                    if (!response.Contains("\"success\":true"))
                        return false;

                    // Extract license data from JSON - using exact field names from Loader_V1
                    string licenseUsername = ExtractJsonString(response, "username");
                    string licenseKey = ExtractJsonString(response, "license_key");
                    string licenseType = ExtractJsonString(response, "type");
                    int isActive = ExtractJsonInt(response, "is_active");
                    long expiryDate = ExtractJsonLong(response, "expiry_date");
                    int daysRemaining = ExtractJsonInt(response, "days_remaining");
                    string boundHWID = ExtractJsonString(response, "bound_hwid");

                    // Verify username matches
                    if (licenseUsername != username)
                        return false;

                    // Verify license key matches
                    if (licenseKey != key)
                        return false;

                    // Check if license is active (THIS IS KEY FOR TERMINATION DETECTION)
                    if (isActive != 1)
                        return false;

                    // Check if license is expired (for non-lifetime licenses)
                    if (licenseType != "lifetime" && expiryDate != 0)
                    {
                        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        if (now > expiryDate)
                            return false;
                    }

                    // Check HWID binding
                    if (!string.IsNullOrEmpty(boundHWID) && boundHWID != hwid)
                        return false;

                    // Update license info
                    _currentLicense.LicenseType = licenseType;
                    _currentLicense.IsPermanent = (licenseType == "lifetime");
                    _currentLicense.DaysRemaining = daysRemaining;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Login with username and license key
        /// </summary>
        public static bool Login(string username, string key)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(key))
                return false;

            if (!ValidateLicenseFormat(key))
                return false;

            string hwid = GenerateHWID();
            bool verified = VerifyLicenseOnline(username, key, hwid);

            if (verified)
            {
                lock (_lockObject)
                {
                    _isAuthenticated = true;
                    _currentLicense.Username = username;
                    _currentLicense.LicenseKey = key;
                    _currentLicense.HWID = hwid;
                    _currentLicense.IsValid = true;
                    _licenseTerminated = false;
                }

                StartBackgroundVerification();
                NotifyAuthStateChanged();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Logout current user
        /// </summary>
        public static void Logout()
        {
            StopBackgroundVerification();

            lock (_lockObject)
            {
                _isAuthenticated = false;
                _currentLicense = new LicenseInfo();
                _licenseTerminated = false;
            }
            NotifyAuthStateChanged();
        }

        /// <summary>
        /// Check if user is authenticated
        /// </summary>
        public static bool IsAuthenticated()
        {
            return _isAuthenticated && !_licenseTerminated;
        }

        /// <summary>
        /// Get current license info
        /// </summary>
        public static LicenseInfo GetLicenseInfo()
        {
            lock (_lockObject)
            {
                return _currentLicense;
            }
        }

        /// <summary>
        /// Check if license is expired
        /// </summary>
        public static bool IsLicenseExpired()
        {
            lock (_lockObject)
            {
                if (_currentLicense.IsPermanent)
                    return false;
                return _currentLicense.DaysRemaining <= 0;
            }
        }

        /// <summary>
        /// Get days remaining on license
        /// </summary>
        public static int GetDaysRemaining()
        {
            lock (_lockObject)
            {
                return _currentLicense.DaysRemaining;
            }
        }

        /// <summary>
        /// Load stored credentials
        /// </summary>
        private static bool LoadCredentials()
        {
            try
            {
                if (!File.Exists(LICENSE_FILE))
                    return false;

                string encryptedData = File.ReadAllText(LICENSE_FILE);
                if (string.IsNullOrEmpty(encryptedData))
                    return false;

                string decrypted = Encryption.DecryptString(encryptedData, ENCRYPTION_KEY);
                string[] parts = decrypted.Split('|');

                if (parts.Length < 5)
                    return false;

                string username = parts[0];
                string key = parts[1];
                string hwid = parts[2];
                bool isPermanent = parts[3] == "1";
                int daysRemaining = int.Parse(parts[4]);
                string licenseType = parts.Length > 5 ? parts[5] : "";

                // Verify HWID matches
                if (!VerifyHWID(hwid))
                    return false;

                // Verify against online API
                if (VerifyLicenseOnline(username, key, hwid))
                {
                    lock (_lockObject)
                    {
                        _isAuthenticated = true;
                        _currentLicense.Username = username;
                        _currentLicense.LicenseKey = key;
                        _currentLicense.HWID = hwid;
                        _currentLicense.IsPermanent = isPermanent;
                        _currentLicense.DaysRemaining = daysRemaining;
                        _currentLicense.LicenseType = licenseType;
                        _currentLicense.IsValid = true;
                        _licenseTerminated = false;
                    }

                    StartBackgroundVerification();
                    NotifyAuthStateChanged();
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Get saved credentials for UI (without authenticating)
        /// </summary>
        public static bool GetSavedCredentials(out string username, out string licenseKey)
        {
            username = "";
            licenseKey = "";

            try
            {
                if (!File.Exists(LICENSE_FILE))
                    return false;

                string encryptedData = File.ReadAllText(LICENSE_FILE);
                if (string.IsNullOrEmpty(encryptedData))
                    return false;

                string decrypted = Encryption.DecryptString(encryptedData, ENCRYPTION_KEY);
                string[] parts = decrypted.Split('|');

                if (parts.Length < 3)
                    return false;

                username = parts[0];
                licenseKey = parts[1];
                string hwid = parts[2];

                // Only return credentials if HWID matches
                if (!VerifyHWID(hwid))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Save credentials with remember me option
        /// </summary>
        public static bool SaveCredentials(bool rememberMe = true)
        {
            if (!_isAuthenticated)
                return false;

            if (!rememberMe)
            {
                try { File.Delete(LICENSE_FILE); } catch { }
                return true;
            }

            try
            {
                lock (_lockObject)
                {
                    string plaintext = string.Format("{0}|{1}|{2}|{3}|{4}|{5}",
                        _currentLicense.Username,
                        _currentLicense.LicenseKey,
                        _currentLicense.HWID,
                        _currentLicense.IsPermanent ? "1" : "0",
                        _currentLicense.DaysRemaining,
                        _currentLicense.LicenseType ?? string.Empty);

                    string encrypted = Encryption.EncryptString(plaintext, ENCRYPTION_KEY);
                    File.WriteAllText(LICENSE_FILE, encrypted);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void NotifyAuthStateChanged()
        {
            try
            {
                AuthStateChanged?.Invoke();
            }
            catch
            {
                // Ignore consumer errors.
            }
        }

        #region JSON Parsing Helpers

        private static string ExtractJsonString(string json, string key)
        {
            string searchKey = "\"" + key + "\":\"";
            int pos = json.IndexOf(searchKey);
            if (pos == -1) return "";

            pos += searchKey.Length;
            int endPos = json.IndexOf("\"", pos);
            if (endPos == -1) return "";

            return json.Substring(pos, endPos - pos);
        }

        private static int ExtractJsonInt(string json, string key)
        {
            string searchKey = "\"" + key + "\":";
            int pos = json.IndexOf(searchKey);
            if (pos == -1) return 0;

            pos += searchKey.Length;
            int endPos = json.IndexOfAny(new char[] { ',', '}' }, pos);
            if (endPos == -1) return 0;

            string numStr = json.Substring(pos, endPos - pos).Trim();
            int result;
            if (int.TryParse(numStr, out result))
                return result;
            return 0;
        }

        private static long ExtractJsonLong(string json, string key)
        {
            string searchKey = "\"" + key + "\":";
            int pos = json.IndexOf(searchKey);
            if (pos == -1) return 0;

            pos += searchKey.Length;
            int endPos = json.IndexOfAny(new char[] { ',', '}' }, pos);
            if (endPos == -1) return 0;

            string numStr = json.Substring(pos, endPos - pos).Trim();
            long result;
            if (long.TryParse(numStr, out result))
                return result;
            return 0;
        }

        #endregion
    }
}
