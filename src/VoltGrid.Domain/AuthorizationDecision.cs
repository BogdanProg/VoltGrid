namespace VoltGrid.Domain;

public enum AuthorizationDecision
{
    Unknown = 0, // default value: no decision was made; an unknown tag is Invalid, not Unknown
    Accepted = 1,
    Blocked = 2,
    Expired = 3,
    Invalid = 4
}