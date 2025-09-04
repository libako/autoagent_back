using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Functions;
using AutoAgentes.Contracts;

namespace AutoAgentes.App.Filters;

/// <summary>
/// Filtro de seguridad para funciones del kernel
/// </summary>
public sealed class GuardrailFilter : IFunctionInvocationFilter
{
    private readonly ITraceEmitter _emitter;
    private readonly string _securityLevel;

    public GuardrailFilter(ITraceEmitter emitter, string securityLevel = "medium")
    {
        _emitter = emitter;
        _securityLevel = securityLevel;
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var functionName = context.Function.Name;
        var sessionId = GetSessionIdFromContext(context);

        // Validar operaciones peligrosas según nivel de seguridad
        if (IsDangerousOperation(functionName))
        {
            var error = $"Operación no permitida: {functionName}";
            await _emitter.EmitAsync(sessionId, new TraceEvent("security_blocked", new { 
                function = functionName, 
                reason = "dangerous_operation",
                securityLevel = _securityLevel 
            }, DateTimeOffset.UtcNow), CancellationToken.None);
            
            throw new UnauthorizedAccessException(error);
        }

        // Validar argumentos según esquema (si está disponible)
        if (context.Arguments != null && context.Arguments.Any())
        {
            await ValidateArgumentsAsync(context, sessionId);
        }

        // Continuar con la ejecución
        await next(context);
    }

    private bool IsDangerousOperation(string functionName)
    {
        var dangerousPatterns = _securityLevel switch
        {
            "high" => new[] { "delete", "remove", "drop", "truncate", "kill", "shutdown" },
            "medium" => new[] { "delete", "remove", "drop" },
            "low" => new[] { "kill", "shutdown" },
            _ => new[] { "delete", "remove", "drop" }
        };

        return dangerousPatterns.Any(pattern => 
            functionName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ValidateArgumentsAsync(FunctionInvocationContext context, Guid sessionId)
    {
        try
        {
            // Aquí se implementaría validación de esquema JSON
            // Por ahora solo validamos que no haya argumentos sospechosos
            foreach (var arg in context.Arguments)
            {
                if (arg.Value?.ToString()?.Contains("script") == true ||
                    arg.Value?.ToString()?.Contains("javascript") == true)
                {
                    await _emitter.EmitAsync(sessionId, new TraceEvent("security_blocked", new { 
                        function = context.Function.Name, 
                        reason = "suspicious_argument",
                        argument = arg.Key 
                    }, DateTimeOffset.UtcNow), CancellationToken.None);
                    
                    throw new UnauthorizedAccessException($"Argumento sospechoso: {arg.Key}");
                }
            }
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException)
        {
            // Log del error pero permitir continuar
            await _emitter.EmitAsync(sessionId, new TraceEvent("validation_warning", new { 
                function = context.Function.Name, 
                warning = ex.Message 
            }, DateTimeOffset.UtcNow), CancellationToken.None);
        }
    }

    private Guid GetSessionIdFromContext(FunctionInvocationContext context)
    {
        // Intentar obtener sessionId del contexto
        if (context.Arguments.TryGetValue("sessionId", out var sessionIdValue) && 
            sessionIdValue is Guid guid)
        {
            return guid;
        }
        
        // Fallback a Guid vacío
        return Guid.Empty;
    }
}
