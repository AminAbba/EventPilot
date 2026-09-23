using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EventPilot.Web;

public sealed class SwaggerAuthorizationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any() || !metadata.OfType<IAuthorizeData>().Any()) return;
        if (metadata.OfType<IAuthorizeData>().Any(x => x.Policy == Auth.AuthorizationPolicies.CanManageEvents))
            operation.Description = (operation.Description + " Requires the Organizer role. Existing-event operations also require ownership.").Trim();
        operation.Security = [new OpenApiSecurityRequirement {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
        }];
    }
}
