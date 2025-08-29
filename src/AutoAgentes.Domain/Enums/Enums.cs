namespace AutoAgentes.Domain.Enums;

public static class DomainEnums
{
    public static class Autonomy
    {
        public const string Manual = "Manual";
        public const string Supervised = "Supervised";
        public const string Auto = "Auto";
    }

    public static class TraceKind
    {
        public const string Plan = "plan";
        public const string ToolCall = "tool_call";
        public const string Observation = "observation";
        public const string Summary = "summary";
        public const string Error = "error";
    }

    public static class AuthType
    {
        public const string None = "none";
        public const string ApiKey = "apikey";
        public const string OAuth = "oauth";
    }
}


