using System.Text.Json.Serialization;
using SmartInput.Core.Integration;

namespace SmartInput.ResidentHost.Services;

// Source-generated metadata keeps the aggregate-only IPC snapshot compatible
// with trimming and NativeAOT. No token or candidate type is serializable here.
[JsonSerializable(typeof(RustShadowAuditSnapshot))]
internal sealed partial class RustShadowAuditJsonContext : JsonSerializerContext
{
}
