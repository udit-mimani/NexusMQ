# NexusMQ - Real-Time Pub/Sub Engine

A high-performance, in-memory Pub/Sub system built with .NET 8. This service provides a real-time messaging interface via **WebSockets** and a management interface via **REST APIs**[cite: 6].

## 🚀 Features
* **WebSocket Protocol**: Supports `subscribe`, `unsubscribe`, `publish`, and `ping` types[cite: 11, 20].
* **RESTful Management**: Endpoints for creating, deleting, and listing topics, along with health and performance metrics[cite: 12, 162].
* **Concurrency**: Thread-safe operations using `ConcurrentDictionary` and `System.Threading.Channels` to handle multiple simultaneous clients safely[cite: 7, 203].
* **Observability**: Integrated **Swagger UI** for easy API testing and a `/stats` endpoint for real-time monitoring[cite: 190, 224].



---

## 🛠 Design Choices & Assumptions

### 1. Backpressure Policy
* **Strategy**: Bounded per-subscriber queues with a **Drop Oldest** policy[cite: 14, 206].
* **Implementation**: Each subscriber has a dedicated `Channel` with a capacity of 50 messages[cite: 206]. If a subscriber's buffer is full (Slow Consumer), the system automatically drops the oldest message to prioritize real-time delivery and prevent memory exhaustion[cite: 134, 206].

### 2. Message Replay (`last_n`)
* **Strategy**: Ring Buffer per Topic[cite: 214].
* **Implementation**: Each topic maintains a local buffer of the last 100 messages[cite: 214]. [cite_start]When a client subscribes with a `last_n` parameter, the system replays up to $N$ historical messages before streaming live data[cite: 31, 214].

### 3. Concurrency & Safety
* **Race-Free**: All topic management uses `ConcurrentDictionary` to ensure stability under multiple clients[cite: 222].
* **Global Stats**: Counters use `Interlocked` operations for thread-safe performance tracking without locking overhead[cite: 203].

### 4. No Persistence
* **State**: As per requirements, all state is stored in-memory only[cite: 7, 8]. [cite_start]Restarting the service clears all topics, subscriptions, and message history[cite: 13].

---

## 🚦 Getting Started

### Prerequisites
* [Podman](https://podman.io/) or [Docker](https://www.docker.com/)[cite: 15].

### Running the Service
1.  **Build the image**:
    ```bash
    podman build -t nexusmq .
    ```
2.  **Run the container**:
    ```bash
    podman run -p 8080:8080 --rm nexusmq
    ```

### API Documentation
Once running, access the interactive **Swagger UI** to test REST endpoints:
`http://localhost:8080/swagger`

---

## 📡 API Reference

### WebSocket (`/ws`)
Connect to `ws://localhost:8080/ws`[cite: 6].

**Example Subscribe Message**:
```json
{
  "type": "subscribe",
  "topic": "orders",
  "client_id": "s1",
  "last_n": 5,
  "request_id": "uuid-123"
}
