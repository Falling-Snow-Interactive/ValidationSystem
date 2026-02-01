using UnityEngine;

namespace Fsi.Validation
{
    public struct ValidatorResult
    {
        public bool Passed { get; set; }
        public string Message { get; set; }

        public ValidatorResult(bool passed, string message = null)
        {
            Passed = passed;
            Message = message;
        }

        public static ValidatorResult Pass(string message = null) => new(true, message);

        public static ValidatorResult Fail(string message = null) => new(false, message);
    }
}
