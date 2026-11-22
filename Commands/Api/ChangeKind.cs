namespace Catalyst.Commands.Api;

/// <summary>The stable codes of <c>api diff</c>, one per kind of change.</summary>
internal static class ChangeKind
{
    public const string OperationRemoved = "OPERATION_REMOVED";
    public const string OperationAdded = "OPERATION_ADDED";
    public const string OperationMoved = "OPERATION_MOVED";
    public const string OperationCallChanged = "OPERATION_CALL_CHANGED";
    public const string ParameterAddedRequired = "PARAMETER_ADDED_REQUIRED";
    public const string ParameterAddedOptional = "PARAMETER_ADDED_OPTIONAL";
    public const string ParameterRemoved = "PARAMETER_REMOVED";
    public const string ParameterBecameRequired = "PARAMETER_BECAME_REQUIRED";
    public const string ParameterBecameOptional = "PARAMETER_BECAME_OPTIONAL";
    public const string ParameterTypeChanged = "PARAMETER_TYPE_CHANGED";
    public const string ParameterMoved = "PARAMETER_MOVED";
    public const string BodyAdded = "BODY_ADDED";
    public const string BodyRemoved = "BODY_REMOVED";
    public const string BodyTypeChanged = "BODY_TYPE_CHANGED";
    public const string BodyMediaTypeChanged = "BODY_MEDIA_TYPE_CHANGED";
    public const string ResponseTypeChanged = "RESPONSE_TYPE_CHANGED";
    public const string TypeRemoved = "TYPE_REMOVED";
    public const string TypeAdded = "TYPE_ADDED";
    public const string TypeKindChanged = "TYPE_KIND_CHANGED";
    public const string TypeAliasChanged = "TYPE_ALIAS_CHANGED";
    public const string TypePropertyRemoved = "TYPE_PROPERTY_REMOVED";
    public const string TypePropertyAddedRequired = "TYPE_PROPERTY_ADDED_REQUIRED";
    public const string TypePropertyAddedOptional = "TYPE_PROPERTY_ADDED_OPTIONAL";
    public const string TypePropertyTypeChanged = "TYPE_PROPERTY_TYPE_CHANGED";
    public const string TypePropertyBecameRequired = "TYPE_PROPERTY_BECAME_REQUIRED";
    public const string TypePropertyBecameOptional = "TYPE_PROPERTY_BECAME_OPTIONAL";
    public const string TypeMemberRemoved = "TYPE_MEMBER_REMOVED";
    public const string TypeMemberAdded = "TYPE_MEMBER_ADDED";
}
