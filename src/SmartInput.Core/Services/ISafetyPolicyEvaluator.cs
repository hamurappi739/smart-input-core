using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public interface ISafetyPolicyEvaluator
{
    AutomationPolicyResult Evaluate(SafetyPolicyContext context);
}
