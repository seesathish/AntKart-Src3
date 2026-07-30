/*
 * AntKart — C4 Architecture (Structurizr DSL)
 *
 * How to use:
 *   Option A (quickest): Paste this into https://structurizr.com/dsl — renders instantly in browser
 *   Option B (local):    docker run -it --rm -p 8080:8080 -v ./docs/architecture:/usr/local/structurizr structurizr/lite
 *                        Open http://localhost:8080 — live reload on save
 *   Option C (export):   structurizr-cli export -workspace workspace.dsl -format plantuml|png|mermaid
 *
 * Structurizr renders proper C4 diagrams with:
 *   - Color-coded boxes (Person=blue, System=grey, Container=blue, DB=green, External=grey-dashed)
 *   - Auto-layout that doesn't overlap
 *   - Clickable zoom: Context → Container → Component
 *   - Multiple views from ONE model (no duplication)
 *
 * ============================================================================
 * OUT OF DATE — scheduled for regeneration (noted 2026-07-23).
 * This model reflects the PRE-MIGRATION topology and does NOT match the platform
 * as built. It still shows a standalone AK.UserIdentity service (retired, ADR-021),
 * AK.Notification as a REST/MailKit service (now a serverless Azure Functions app,
 * ADR-019), Keycloak (now Microsoft Entra ID), RabbitMQ (now Azure Service Bus),
 * ELK (now Azure Monitor / Application Insights), and stores that differ from the
 * managed Azure services now used. The current platform has SIX deployable services
 * plus the serverless notifications app. Until this DSL is updated and re-exported,
 * the README, docs/ROADMAP.md, and the ADRs are the authoritative current-state source.
 * ============================================================================
 */

workspace "AntKart" "Cloud-native .NET 9 microservices e-commerce platform" {

    !identifiers hierarchical

    model {

        // ── People ──
        customer = person "Customer" "Browses products, manages cart, places orders, pays" {
            tags "Person"
        }
        admin = person "Administrator" "Manages product catalogue, monitors orders, assigns roles" {
            tags "Person"
        }

        // ── External Systems ──
        keycloak = softwareSystem "Keycloak 24" "Open-source identity provider. Hosts the antkart realm with user/admin roles. Issues JWT tokens." {
            tags "External"
        }
        razorpay = softwareSystem "Razorpay" "Payment gateway. Creates order objects, processes card payments, returns HMAC-SHA256 signatures." {
            tags "External"
        }
        smtp = softwareSystem "SMTP / Mailhog" "Email delivery. Mailhog SMTP trap in dev; Gmail SMTP in production." {
            tags "External"
        }
        elk = softwareSystem "ELK Stack" "Observability. Elasticsearch stores structured JSON logs. Kibana provides dashboards." {
            tags "External"
        }
        rabbitmq = softwareSystem "RabbitMQ 3" "AMQP message broker. Fans out integration events. Hosts SAGA state machine queues." {
            tags "External"
        }

        // ── AntKart Platform ──
        antkart = softwareSystem "AntKart Platform" "Cloud-native .NET 9 microservices e-commerce platform with 8 services, SAGA orchestration, CQRS, and event-driven architecture." {

            // ── Containers ──
            gateway = container "AK.Gateway" "Single entry point. JWT passthrough, per-route rate limiting (10-30 RPS), QoS circuit breaker." "Ocelot 23.4 · .NET 9 · port 9090" {
                tags "Service"
            }

            identity = container "AK.UserIdentity" "Keycloak proxy. Login, register, token refresh, /me, admin user list, role assignment." "Minimal API · .NET 9 · port 8084" {
                tags "Service"
            }

            products = container "AK.Products" "Product catalogue. CRUD with category filtering. Calls Discount via gRPC. Consumes OrderCreated to reserve stock." "Minimal API · .NET 9 · port 8080" {
                tags "Service"
            }

            discount = container "AK.Discount" "Discount coupon service. Exposes GetDiscount RPC. 300 seed coupons keyed by SKU." "gRPC · .NET 9 · port 8081" {
                tags "Service"
            }

            cart = container "AK.ShoppingCart" "Shopping cart. Add, update, remove, clear. JSON snapshots in Redis with 30-day TTL." "Minimal API · .NET 9 · port 8082" {
                tags "Service"
            }

            order = container "AK.Order" "Order lifecycle. SAGA for stock reservation + payment. CQRS + MediatR. EF Core Outbox." "Minimal API · .NET 9 · port 8083" {
                tags "Service"

                // ── Level 3: Components inside AK.Order ──
                endpoints = component "OrderEndpoints" "Maps HTTP verbs to MediatR Send() calls. UserId from JWT sub claim. Admin-only on PUT /status." "Minimal API · MapGroup"
                middleware = component "ExceptionHandlerMiddleware" "ValidationException→400, KeyNotFound→404, InvalidOp→409, Exception→500" "ASP.NET Middleware"
                mediator_comp = component "MediatR" "Dispatches IRequest to exactly one handler. Runs ValidationBehavior pipeline." "In-process command bus"
                validation = component "ValidationBehavior" "Pre-handler FluentValidation. Throws ValidationException → 400." "MediatR Pipeline Behavior"
                create_cmd = component "CreateOrderCommandHandler" "Creates Order aggregate. Outbox publishes OrderCreatedIntegrationEvent." "Command Handler"
                update_cmd = component "UpdateOrderStatusCommandHandler" "Returns Result<OrderDto>. Domain state machine enforces transitions." "Command Handler · Result<T>"
                cancel_cmd = component "CancelOrderCommandHandler" "Returns Result<bool>. Publishes OrderCancelledIntegrationEvent." "Command Handler · Result<T>"
                get_queries = component "Query Handlers" "GetOrderById, GetOrders, GetOrdersByUser. Specification pattern for filters." "Query Handlers"
                saga = component "OrderSaga" "States: Initial → StockPending → Confirmed|Cancelled. Persisted to PostgreSQL." "MassTransit StateMachine"
                consumers = component "Event Consumers" "PaymentSucceeded, PaymentFailed, OrderConfirmed, OrderCancelled consumers." "MassTransit Consumers"
                repo = component "OrderRepository" "EF Core IQueryable. GetByIdAsync, FindAsync with ISpecification." "Repository Pattern"
                uow = component "UnitOfWork" "SaveChangesAsync dispatches domain events via Outbox. Atomic: row + outbox." "Unit of Work"
                dbctx = component "OrderDbContext" "Orders, OrderItems, SAGA state, Outbox tables. Fluent API config." "EF Core 9 DbContext"
            }

            payments = container "AK.Payments" "Payment processing. Razorpay orders, HMAC-SHA256 verification, saved cards (token IDs only)." "Minimal API · .NET 9 · port 8085" {
                tags "Service"
            }

            notification = container "AK.Notification" "Event-driven emails. 6 consumers, HTML templates, MailKit dispatch. 90-day cleanup." "Minimal API · .NET 9 · port 8086" {
                tags "Service"
            }

            // ── Databases ──
            mongodb = container "MongoDB" "Document store for product catalogue. StringEntity IDs." "AKProductsDb" {
                tags "Database"
            }
            sqlite = container "SQLite" "Relational store for coupon records. EF Core migrations." "discount.db" {
                tags "Database"
            }
            redis = container "Redis" "In-memory key-value store. One key per user cart. 30-day TTL." "AKCart" {
                tags "Database"
            }
            pg_orders = container "PostgreSQL (Orders)" "Orders, items, SAGA state, Outbox messages." "AKOrdersDb · EF Core 9 · Npgsql" {
                tags "Database"
            }
            pg_payments = container "PostgreSQL (Payments)" "Payment records and saved card tokens." "AKPaymentsDb · EF Core 9 · Npgsql" {
                tags "Database"
            }
            pg_notifications = container "PostgreSQL (Notifications)" "Notification delivery history." "AKNotificationsDb · EF Core 9 · Npgsql" {
                tags "Database"
            }
        }

        // ── Level 1: System Context relationships ──
        customer -> antkart "Shops, places orders, pays" "HTTPS · REST/JSON"
        admin -> antkart "Manages catalogue and orders" "HTTPS · REST/JSON"
        antkart -> keycloak "Authenticates via OIDC" "HTTPS"
        antkart -> razorpay "Initiates payments, verifies signatures" "HTTPS · REST"
        antkart -> smtp "Sends transactional emails" "SMTP · MailKit"
        antkart -> elk "Ships structured JSON logs" "HTTP · Serilog"
        antkart -> rabbitmq "Publishes/consumes integration events" "AMQP · MassTransit"

        // ── Level 2: Container relationships ──
        customer -> antkart.gateway "All requests" "HTTPS · port 9090"
        admin -> antkart.gateway "All admin requests" "HTTPS · port 9090"

        antkart.gateway -> antkart.identity "Routes /api/auth/** · /api/admin/**" "HTTP"
        antkart.gateway -> antkart.products "Routes /api/v1/products/**" "HTTP"
        antkart.gateway -> antkart.cart "Routes /api/v1/cart/**" "HTTP"
        antkart.gateway -> antkart.order "Routes /api/orders/**" "HTTP"
        antkart.gateway -> antkart.payments "Routes /api/payments/**" "HTTP"
        antkart.gateway -> antkart.notification "Routes /gateway/notifications/**" "HTTP"

        antkart.identity -> keycloak "Token issue, user CRUD, role assignment" "HTTPS · Keycloak Admin API"
        antkart.gateway -> keycloak "Validates JWT on protected routes" "HTTPS · JWKS"

        antkart.products -> antkart.discount "GetDiscount(sku)" "gRPC · proto3"
        antkart.products -> antkart.mongodb "CRUD products" "MongoDB.Driver 3.3"
        antkart.discount -> antkart.sqlite "CRUD coupons" "EF Core 9"
        antkart.cart -> antkart.redis "Get/Set cart snapshots" "StackExchange.Redis"
        antkart.order -> antkart.pg_orders "CRUD orders · SAGA · Outbox" "EF Core 9 · Npgsql"
        antkart.payments -> antkart.pg_payments "CRUD payments · saved cards" "EF Core 9 · Npgsql"
        antkart.notification -> antkart.pg_notifications "Persist notifications" "EF Core 9 · Npgsql"

        antkart.payments -> razorpay "Create order, verify signature" "HTTPS · Razorpay SDK"
        antkart.notification -> smtp "Send HTML emails" "SMTP · MailKit"

        antkart.order -> rabbitmq "Publishes: OrderCreated, OrderConfirmed, OrderCancelled" "AMQP · MassTransit Outbox"
        antkart.payments -> rabbitmq "Publishes: PaymentInitiated, PaymentSucceeded, PaymentFailed" "AMQP · MassTransit"
        antkart.identity -> rabbitmq "Publishes: UserRegistered" "AMQP · MassTransit"
        rabbitmq -> antkart.products "Consumes: OrderCreated → reserve stock" "AMQP"
        rabbitmq -> antkart.order "Consumes: StockReserved, StockFailed, PaymentSucceeded, PaymentFailed" "AMQP · SAGA + consumers"
        rabbitmq -> antkart.notification "Consumes: 6 integration events → email" "AMQP"

        // ── Level 3: Component relationships (AK.Order) ──
        antkart.gateway -> antkart.order.endpoints "HTTP verbs" "REST/JSON · JWT Bearer"
        antkart.order.endpoints -> antkart.order.mediator_comp "Send(command | query)"
        antkart.order.mediator_comp -> antkart.order.validation "Pipeline step"
        antkart.order.mediator_comp -> antkart.order.create_cmd "CreateOrderCommand"
        antkart.order.mediator_comp -> antkart.order.update_cmd "UpdateOrderStatusCommand"
        antkart.order.mediator_comp -> antkart.order.cancel_cmd "CancelOrderCommand"
        antkart.order.mediator_comp -> antkart.order.get_queries "Queries"
        antkart.order.create_cmd -> antkart.order.uow "AddAsync + SaveChangesAsync"
        antkart.order.update_cmd -> antkart.order.uow "UpdateAsync + SaveChangesAsync"
        antkart.order.cancel_cmd -> antkart.order.uow "UpdateAsync + SaveChangesAsync"
        antkart.order.get_queries -> antkart.order.repo "GetByIdAsync · FindAsync(spec)"
        antkart.order.repo -> antkart.order.dbctx "EF Core queries"
        antkart.order.uow -> antkart.order.dbctx "SaveChangesAsync + Outbox enqueue"
        antkart.order.dbctx -> antkart.pg_orders "SQL via Npgsql"
        antkart.order.dbctx -> rabbitmq "Outbox relay forwards events" "AMQP"
        rabbitmq -> antkart.order.saga "OrderCreated, StockReserved, StockFailed" "AMQP"
        antkart.order.saga -> antkart.order.dbctx "Reads/writes SAGA state"
        antkart.order.saga -> rabbitmq "Publishes OrderConfirmed/OrderCancelled" "AMQP"
        rabbitmq -> antkart.order.consumers "Payment + Order events" "AMQP"
        antkart.order.consumers -> antkart.order.mediator_comp "Send(UpdateOrderStatusCommand)"
    }

    views {

        // Level 1: System Context
        systemContext antkart "SystemContext" "Level 1 — Who uses AntKart and what does it depend on?" {
            include *
            autoLayout
        }

        // Level 2: Container
        container antkart "Containers" "Level 2 — Deployable units and their communication" {
            include *
            autoLayout
        }

        // Level 3: Component (AK.Order)
        component antkart.order "OrderComponents" "Level 3 — Inside AK.Order: CQRS, SAGA, Outbox" {
            include *
            autoLayout
        }

        // Dynamic: Order happy-path flow
        dynamic antkart "OrderFlow" "Happy-path order placement: Create → Stock → Payment → Confirm" {
            customer -> antkart.gateway "POST /api/orders (JWT)"
            antkart.gateway -> antkart.order "Route to AK.Order"
            antkart.order -> rabbitmq "Outbox → OrderCreatedIntegrationEvent"
            rabbitmq -> antkart.products "OrderCreated → reserve stock"
            antkart.products -> rabbitmq "StockReservedIntegrationEvent"
            rabbitmq -> antkart.order "StockReserved → SAGA confirms"
            antkart.order -> rabbitmq "OrderConfirmedIntegrationEvent"
            rabbitmq -> antkart.notification "OrderConfirmed → email"
            customer -> antkart.gateway "POST /api/payments/initiate"
            antkart.gateway -> antkart.payments "Route to AK.Payments"
            antkart.payments -> razorpay "Create Razorpay order"
            customer -> antkart.gateway "POST /api/payments/verify"
            antkart.gateway -> antkart.payments "Verify HMAC signature"
            antkart.payments -> rabbitmq "PaymentSucceededIntegrationEvent"
            rabbitmq -> antkart.order "PaymentSucceeded → UpdateStatus(Paid)"
            rabbitmq -> antkart.notification "PaymentSucceeded → receipt email"
            autoLayout
        }

        // ── Styling ──
        styles {
            element "Person" {
                shape Person
                background #08427B
                color #ffffff
            }
            element "Software System" {
                background #1168BD
                color #ffffff
            }
            element "External" {
                background #999999
                color #ffffff
            }
            element "Service" {
                background #438DD5
                color #ffffff
            }
            element "Database" {
                shape Cylinder
                background #85BBF0
                color #000000
            }
            element "Component" {
                background #85BBF0
                color #000000
            }
            relationship "Relationship" {
                dashed false
            }
        }
    }

}
