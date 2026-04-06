using System;

namespace RemoteInspector
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RemoteCommandAttribute : Attribute
    {
        public RemoteCommandAttribute(string category = "General", string alias = "", string description = "")
        {
            Category = category ?? "General";
            Alias = alias ?? string.Empty;
            Description = description ?? string.Empty;
        }

        public string Category { get; }

        public string Alias { get; }

        public string Description { get; }
    }
}
