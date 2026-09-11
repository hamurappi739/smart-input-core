using System.Text.Json.Serialization;
using SmartInput.Core.Integration;

namespace SmartInput.ResidentHost.Services;

[JsonSerializable(typeof(ResidentRuntimeStatusSnapshot))]
internal sealed partial class ResidentRuntimeStatusJsonContext : JsonSerializerContext
{
}
