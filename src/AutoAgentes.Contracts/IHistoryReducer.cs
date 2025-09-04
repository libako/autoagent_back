namespace AutoAgentes.Contracts;

public interface IHistoryReducer
{
    object Apply(object input, int maxTokens = 4000);
}

