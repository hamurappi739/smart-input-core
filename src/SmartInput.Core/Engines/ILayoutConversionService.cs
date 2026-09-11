namespace SmartInput.Core.Engines;

public interface ILayoutConversionService
{
    string Convert(string input, LayoutConversionDirection direction);
}
