using System;
using JetBrains.Annotations;

namespace Fsi.Validation
{
    /// <summary>
    /// Marks a static validator method for discovery by <see cref="ValidatorRegistry" />.
    /// Contract: method must be <c>static</c>, parameterless, and return either
    /// <c>bool</c> (pass/fail) or <see cref="ValidatorResult" /> (pass/fail plus message).
    /// </summary>
    [MeansImplicitUse(ImplicitUseKindFlags.Access)]
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValidationMethod : Attribute
    {
    }
}
