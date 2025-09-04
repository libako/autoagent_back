# Contexto Conversacional con Semantic Kernel

## Resumen

Se ha implementado un sistema completo de **contexto conversacional real** para tu AutoAgente, siguiendo las mejores prácticas de Semantic Kernel y tu arquitectura técnica.

## Componentes Implementados

### 1. **IConversationStore** + **ConversationStore**
- Persiste y lee historial de conversación por `sessionId`
- Soporta diferentes tipos de mensaje (`user`, `assistant`, `system`, `observation`)
- Usa tu BD existente con el nuevo campo `Kind` en la entidad `Message`

### 2. **IContextBuilder** + **ContextBuilder**
- Construye `ChatHistory` con contexto real de la conversación
- Inyecta system prompt del agente
- Incluye observaciones de tools del turno actual
- Aplica reducers para mantener presupuesto de tokens

### 3. **IHistoryReducer** + Implementaciones
- **LastKMessagesReducer**: Mantiene solo los últimos N mensajes
- **WhiteboardReducer**: Extrae hechos clave y mantiene contexto breve

### 4. **Planner Actualizado**
- Ahora usa `ChatHistory` en lugar de prompts vacíos
- Mantiene coherencia con el contexto de la conversación
- Retorna `RegistryVersion` para validación de herramientas

### 5. **OrchestratorService Mejorado**
- Construye contexto antes de planificar
- Recopila observaciones durante la ejecución
- Genera respuesta final usando contexto real

## Flujo End-to-End

```
1. Usuario envía mensaje
   ↓
2. ContextBuilder construye ChatHistory con:
   - System prompt del agente
   - Mensajes previos de la conversación
   - Observaciones del turno anterior
   ↓
3. Planner crea plan usando ChatHistory (con contexto real)
   ↓
4. Orchestrator ejecuta pasos y recopila observaciones
   ↓
5. ContextBuilder reconstruye ChatHistory para respuesta final
   ↓
6. Se genera respuesta usando todo el contexto
   ↓
7. Se persiste en ConversationStore
```

## Configuración

### 1. Registrar Servicios
```csharp
// En Program.cs o Startup.cs
services.AddConversationContext();
```

### 2. Uso en OrchestratorService
```csharp
// El servicio ya está actualizado para usar:
var contextBuilder = _serviceProvider.GetRequiredService<IContextBuilder>();
var history = await contextBuilder.BuildAsync(agent, sessionId, userMessage, currentObservations, ct);
```

## Beneficios Implementados

✅ **Contexto Real**: El planner y respuesta final usan historial real de la conversación
✅ **Presupuesto de Tokens**: Reducers mantienen el contexto dentro de límites
✅ **Memoria de Corto Plazo**: Whiteboard extrae decisiones/hechos clave
✅ **Determinismo**: Configuración LLM optimizada para planificación
✅ **Trazabilidad**: Métricas detalladas de contexto y reducción
✅ **Compatibilidad MCP**: Funciona con tu registry dinámico existente

## Métricas y Observabilidad

- `context_builder_done`: Historial antes/después, tipo de reducer
- `planner_called`: Tokens de entrada/salida, versión del registry
- `tool_call`: Latencia, éxito, correlación de trazas

## Próximos Pasos Recomendados

1. **Implementar reintentos** con backoff exponencial para errores transitorios
2. **Añadir validación de JSON Schema** usando NJsonSchema o similar
3. **Implementar rate limiting** por servidor MCP
4. **Añadir métricas de negocio** (tiempo total, pasos exitosos, etc.)
5. **Implementar circuit breaker** para servidores MCP problemáticos

## Notas Técnicas

- **Campo `Kind`**: Se añadió a la entidad `Message` para distinguir tipos
- **Reducers**: Configurables por presupuesto de tokens
- **Working Memory**: Mantiene outputs de pasos para chaining
- **Registry Versioning**: Preparado para futuras implementaciones

## Testing

```csharp
// Ejemplo de test unitario
[Test]
public async Task ContextBuilder_Should_Include_Agent_SystemPrompt()
{
    var agent = new Agent { Name = "TestAgent", Autonomy = "Supervised" };
    var contextBuilder = new ContextBuilder(/* dependencies */);
    
    var history = await contextBuilder.BuildAsync(agent, sessionId, "test", Array.Empty<(string, string)>(), CancellationToken.None);
    
    Assert.That(history.Any(m => m.Role == "system" && m.Content.Contains("TestAgent")));
}
```

## Compatibilidad

- ✅ .NET 8.0
- ✅ Semantic Kernel (versión actual)
- ✅ Entity Framework Core
- ✅ Tu arquitectura MCP existente
- ✅ Sistema de trazas actual

