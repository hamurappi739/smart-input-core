using SmartInput.Core.Models;

namespace SmartInput.Core.Services;

public static class SafetyStatusFormatter
{
    public static SafetyStatusSummary Create(
        AppSettings settings,
        AutomationPolicyResult policy,
        bool isMonitoring)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(policy);

        if (settings.EmergencyPauseEnabled)
        {
            return new SafetyStatusSummary
            {
                Headline = "Включена экстренная пауза",
                Detail = "Автоматизация и внешние операции с текстом выключены, пока вы не отключите экстренную паузу.",
                Level = SafetyStatusLevel.Paused,
                IsAutomationAllowed = false,
                PolicyState = policy.State,
                IsProtectionEnabled = settings.IsEnabled,
                IsEmergencyPauseEnabled = true,
                IsMonitoringActive = isMonitoring,
            };
        }

        if (!settings.IsEnabled)
        {
            return new SafetyStatusSummary
            {
                Headline = "Защита выключена",
                Detail = "Smart Input не наблюдает за вводом с клавиатуры. Включите защиту на главной странице, чтобы активировать помощь.",
                Level = SafetyStatusLevel.Inactive,
                IsAutomationAllowed = false,
                PolicyState = policy.State,
                IsProtectionEnabled = false,
                IsEmergencyPauseEnabled = false,
                IsMonitoringActive = false,
            };
        }

        return policy.State switch
        {
            AutomationPolicyState.Allowed when policy.AllowsAutomation =>
                new SafetyStatusSummary
                {
                    Headline = "Автоматизация разрешена",
                    Detail = "Активное приложение разрешает автоматическое исправление раскладки. Защищённые поля по-прежнему защищены.",
                    Level = SafetyStatusLevel.Normal,
                    IsAutomationAllowed = true,
                    PolicyState = policy.State,
                    IsProtectionEnabled = true,
                    IsEmergencyPauseEnabled = false,
                    IsMonitoringActive = isMonitoring,
                },
            AutomationPolicyState.ProtectionDisabled =>
                new SafetyStatusSummary
                {
                    Headline = "Защита выключена",
                    Detail = policy.Reason ?? "Smart Input не выполняет автоматические или внешние операции с текстом.",
                    Level = SafetyStatusLevel.Inactive,
                    IsAutomationAllowed = false,
                    PolicyState = policy.State,
                    IsProtectionEnabled = false,
                    IsEmergencyPauseEnabled = false,
                    IsMonitoringActive = false,
                },
            AutomationPolicyState.SafeMode =>
                new SafetyStatusSummary
                {
                    Headline = "Активен безопасный режим",
                    Detail = policy.Reason
                        ?? "Этот контекст считается чувствительным: терминал, игровое окно, песочница или удалённый рабочий стол. Автоматическое исправление заблокировано; ручные действия остаются доступны.",
                    Level = SafetyStatusLevel.Caution,
                    IsAutomationAllowed = false,
                    PolicyState = policy.State,
                    IsProtectionEnabled = true,
                    IsEmergencyPauseEnabled = false,
                    IsMonitoringActive = isMonitoring,
                },
            AutomationPolicyState.BlockedApplication =>
                new SafetyStatusSummary
                {
                    Headline = "Приложение исключено",
                    Detail = policy.Reason
                        ?? "Это приложение находится в списке исключений. Автоматическое исправление заблокировано.",
                    Level = SafetyStatusLevel.Restricted,
                    IsAutomationAllowed = false,
                    PolicyState = policy.State,
                    IsProtectionEnabled = true,
                    IsEmergencyPauseEnabled = false,
                    IsMonitoringActive = isMonitoring,
                },
            AutomationPolicyState.SecureInput =>
                new SafetyStatusSummary
                {
                    Headline = "Обнаружен защищённый ввод",
                    Detail = policy.Reason
                        ?? "Поля пароля и защищённого ввода блокируют автоматизацию. Это нельзя обойти в настройках.",
                    Level = SafetyStatusLevel.Restricted,
                    IsAutomationAllowed = false,
                    PolicyState = policy.State,
                    IsProtectionEnabled = true,
                    IsEmergencyPauseEnabled = false,
                    IsMonitoringActive = isMonitoring,
                },
            _ =>
                new SafetyStatusSummary
                {
                    Headline = "Контекст ограничен",
                    Detail = policy.Reason ?? "Автоматизация заблокирована в текущем контексте.",
                    Level = SafetyStatusLevel.Caution,
                    IsAutomationAllowed = policy.AllowsAutomation,
                    PolicyState = policy.State,
                    IsProtectionEnabled = true,
                    IsEmergencyPauseEnabled = false,
                    IsMonitoringActive = isMonitoring,
                },
        };
    }
}
