# Resumen del Protocolo MCP Implementado

## 1. Descubrimiento de Herramientas (HTTP)

### Request:
```json
{
  "jsonrpc": "2.0",
  "id": "guid-unico",
  "method": "tools/list",
  "params": {}
}
```

### Response Esperada:
```json
{
  "jsonrpc": "2.0",
  "id": "guid-unico",
  "result": {
    "tools": [
      {
        "name": "nombre-herramienta",
        "description": "descripción",
        "scope": "global",
        "inputSchema": { ... }
      }
    ]
  }
}
```

## 2. Llamada a Herramienta (HTTP)

### Request:
```json
{
  "jsonrpc": "2.0",
  "id": "guid-unico",
  "method": "tools/call",
  "params": {
    "name": "nombre-herramienta",
    "arguments": {
      "prompt": "mensaje del usuario"
    }
  }
}
```

### Response Esperada:
```json
{
  "jsonrpc": "2.0",
  "id": "guid-unico",
  "result": "resultado de la herramienta"
}
```

## 3. Descubrimiento de Herramientas (WebSocket)

### Secuencia:
1. **Initialize**:
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2024-11-05",
    "capabilities": { "tools": {} },
    "clientInfo": {
      "name": "AutoAgentes",
      "version": "1.0.0"
    }
  }
}
```

2. **Initialized Notification**:
```json
{
  "jsonrpc": "2.0",
  "method": "notifications/initialized",
  "params": {}
}
```

3. **Tools List**:
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/list",
  "params": {}
}
```

## 4. Llamada a Herramienta (WebSocket)

### Secuencia:
1. **Initialize** (igual que arriba)
2. **Initialized Notification** (igual que arriba)
3. **Tools Call**:
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "nombre-herramienta",
    "arguments": {
      "prompt": "mensaje del usuario"
    }
  }
}
```

## 5. Endpoints HTTP Disponibles

- `POST /mcp/servers` - Crear servidor MCP
- `GET /mcp/servers` - Listar servidores
- `POST /mcp/servers/{id}/discover` - Descubrir herramientas
- `POST /mcp/servers/{id}/refresh` - Refrescar herramientas
- `GET /mcp/tools?serverId={id}` - Listar herramientas de un servidor
- `GET /mcp/tools/all` - Listar todas las herramientas

## 6. Flujo de Ejecución

1. **Descubrimiento**: HTTP → WebSocket fallback
2. **Almacenamiento**: Herramientas se guardan en BD
3. **Vinculación**: Agentes se vinculan con herramientas
4. **Ejecución**: Llamadas HTTP con JSON-RPC 2.0
5. **Fallback**: WebSocket si HTTP falla

## 7. Manejo de Errores

- **HTTP 400**: Formato incorrecto o parámetros inválidos
- **WebSocket 101**: Upgrade fallido
- **Timeout**: 30 segundos para respuestas
- **Reintentos**: 3 intentos con backoff exponencial

## 8. Validaciones

- ✅ Formato JSON-RPC 2.0
- ✅ Protocolo MCP 2024-11-05
- ✅ Manejo de respuestas y errores
- ✅ Fallback entre HTTP y WebSocket
- ✅ Sincronización con base de datos

