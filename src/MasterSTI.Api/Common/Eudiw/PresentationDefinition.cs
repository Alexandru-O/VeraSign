namespace MasterSTI.Api.Common.Eudiw;

public static class PresentationDefinition
{
    /// <summary>Builds a PID (Person Identification Data) input descriptor for OpenID4VP.</summary>
    public static PresentationDefinitionModel BuildPid() => new(
        Id: "pid-request",
        InputDescriptors: new[]
        {
            new InputDescriptor(
                Id: "eu.europa.ec.eudi.pid.1",
                Name: "EU PID Credential",
                Constraints: new ConstraintsModel(new[]
                {
                    new FieldConstraint(new[] { "$.family_name" }, true),
                    new FieldConstraint(new[] { "$.given_name" }, true),
                    new FieldConstraint(new[] { "$.birth_date" }, false)
                }))
        });
}
