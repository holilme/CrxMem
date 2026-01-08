using System;

namespace CrxMem.Core
{
    /// <summary>
    /// Represents the detection status of a bypass test
    /// </summary>
    public enum DetectionStatus
    {
        /// <summary>Test has not been run yet</summary>
        NotRun,
        /// <summary>Bypass was successful - not detected</summary>
        Undetected,
        /// <summary>Bypass was partially successful - some detection occurred</summary>
        PartialDetection,
        /// <summary>Bypass failed - fully detected/blocked</summary>
        Blocked,
        /// <summary>Test encountered an error during execution</summary>
        Error
    }

    /// <summary>
    /// Risk level associated with a bypass technique
    /// </summary>
    public enum BypassRiskLevel
    {
        /// <summary>Low risk - minimal system impact</summary>
        Low,
        /// <summary>Medium risk - moderate system impact</summary>
        Medium,
        /// <summary>High risk - significant system impact, could cause instability</summary>
        High,
        /// <summary>Critical risk - could cause BSOD or system corruption</summary>
        Critical
    }

    /// <summary>
    /// Represents the result of a bypass test execution
    /// </summary>
    public class BypassTestResult
    {
        /// <summary>
        /// Name of the test that was executed
        /// </summary>
        public string TestName { get; set; } = string.Empty;

        /// <summary>
        /// Description of what the test does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Risk level of the test
        /// </summary>
        public BypassRiskLevel RiskLevel { get; set; } = BypassRiskLevel.Low;

        /// <summary>
        /// Detection status after running the test
        /// </summary>
        public DetectionStatus Status { get; set; } = DetectionStatus.NotRun;

        /// <summary>
        /// Detailed message about the test result
        /// </summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// How long the test took to execute
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// When the test was executed
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Exception that occurred during test (if any)
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Gets a display string for the status
        /// </summary>
        public string StatusDisplay => Status switch
        {
            DetectionStatus.NotRun => "Not Run",
            DetectionStatus.Undetected => "Undetected",
            DetectionStatus.PartialDetection => "Partial",
            DetectionStatus.Blocked => "Blocked",
            DetectionStatus.Error => "Error",
            _ => "Unknown"
        };

        /// <summary>
        /// Gets a status icon for display
        /// </summary>
        public string StatusIcon => Status switch
        {
            DetectionStatus.NotRun => "?",
            DetectionStatus.Undetected => "\u2713", // Checkmark
            DetectionStatus.PartialDetection => "\u26A0", // Warning
            DetectionStatus.Blocked => "\u2717", // X
            DetectionStatus.Error => "\u2716", // Heavy X
            _ => "?"
        };

        /// <summary>
        /// Gets the duration as a display string
        /// </summary>
        public string DurationDisplay => Duration.TotalMilliseconds < 1000
            ? $"{Duration.TotalMilliseconds:F0}ms"
            : $"{Duration.TotalSeconds:F2}s";

        /// <summary>
        /// Creates a successful (undetected) test result
        /// </summary>
        public static BypassTestResult Success(string testName, string details, TimeSpan duration)
        {
            return new BypassTestResult
            {
                TestName = testName,
                Status = DetectionStatus.Undetected,
                Details = details,
                Duration = duration,
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// Creates a blocked test result
        /// </summary>
        public static BypassTestResult Fail(string testName, string details, TimeSpan duration)
        {
            return new BypassTestResult
            {
                TestName = testName,
                Status = DetectionStatus.Blocked,
                Details = details,
                Duration = duration,
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// Creates an error test result
        /// </summary>
        public static BypassTestResult Error(string testName, Exception ex, TimeSpan duration)
        {
            return new BypassTestResult
            {
                TestName = testName,
                Status = DetectionStatus.Error,
                Details = ex.Message,
                Exception = ex,
                Duration = duration,
                Timestamp = DateTime.Now
            };
        }
    }

    /// <summary>
    /// Represents a bypass technique that can be tested
    /// </summary>
    public class BypassTechnique
    {
        /// <summary>
        /// Unique identifier for the technique
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the technique
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Description of what the technique does
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Risk level associated with this technique
        /// </summary>
        public BypassRiskLevel RiskLevel { get; set; } = BypassRiskLevel.Low;

        /// <summary>
        /// Whether this technique requires a process to be attached
        /// </summary>
        public bool RequiresProcess { get; set; } = false;

        /// <summary>
        /// Whether this technique requires the kernel driver to be loaded
        /// </summary>
        public bool RequiresDriver { get; set; } = false;

        /// <summary>
        /// Whether this technique requires administrator privileges
        /// </summary>
        public bool RequiresAdmin { get; set; } = false;

        /// <summary>
        /// Category of the technique (User-mode, Kernel-mode, etc.)
        /// </summary>
        public string Category { get; set; } = "User-mode";
    }
}
