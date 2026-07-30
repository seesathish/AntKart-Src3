/*
 * AntKart — Platform architecture (hero diagram)
 *
 * Question: how is the code built?
 * Shows the engineering patterns inside ONE service (AK.Order, the richest example),
 * grouped as the four Clean Architecture layers. The point is the dependency rule:
 * every arrow points INWARD toward the Domain. Deliberately excludes Azure services,
 * the other five services, and anything infrastructural — those are other diagrams.
 *
 * RENDER
 *   docker run -it --rm -p 8080:8080 \
 *     -v "C:\Users\seesa\OneDrive\Desktop\AntCart\AntKart-Src3\docs\C4Renders\hero-platform:/usr/local/structurizr" \
 *     structurizr/structurizr local
 *   Open http://localhost:8080. Edit, save, press F5. Never restart the container.
 *
 * TWO-PHASE WORKFLOW
 *   Phase one: autoLayout is ON (see the view) to get the rough left-to-right flow.
 *   Phase two: COMMENT OUT the autoLayout line, refresh, then hand-arrange so the
 *              four layers read API → Application → Domain, with Infrastructure
 *              pointing back inward at the Domain.
 *
 * IMPORTANT — autoLayout WARNING
 *   autoLayout recalculates on every load and DISCARDS hand placement. Once you start
 *   dragging, leave autoLayout commented out permanently. Placement autosaves into
 *   workspace.json (generated — do not hand-edit; .structurizr/ is gitignored cache).
 */

workspace "AntKart — platform architecture" "How the code is built: Clean Architecture layers with dependencies pointing inward" {

    !identifiers hierarchical

    model {

        antkart = softwareSystem "AK.Order — representative service" "The richest AntKart service. Every service shares this Clean Architecture shape, so understanding one is understanding all." {

            // ── Outermost: the delivery layer ─────────────────────────────────
            group "API" {
                endpoints = container "Order endpoints" "Minimal API endpoints. Read the user from the JWT, dispatch to MediatR, map the result to HTTP." ".NET 9 Minimal API" {
                    tags "Service"
                }
                exceptions = container "Exception middleware" "Maps domain exceptions (validation, not-found, forbidden, invalid transition) to status codes." "AK.BuildingBlocks" {
                    tags "Service"
                }
            }

            // ── Use cases ──────────────────────────────────────────────────────
            group "Application" {
                mediatr = container "MediatR pipeline" "Dispatches commands and queries through the shared ValidationBehavior before a handler runs." "MediatR 12 · FluentValidation" {
                    tags "Service"
                }
                commands = container "Command handlers" "CreateOrder, CancelOrder, UpdateOrderStatus. Return DTOs or Result<T> — never entities." "CQRS write side" {
                    tags "Service"
                }
                queries = container "Query handlers" "GetOrderById, GetOrders, GetOrdersByUser. Return DTOs / PagedResult<T>." "CQRS read side" {
                    tags "Service"
                }
                saga = container "Order saga state machine" "OrderSaga + OrderSagaState: orchestrates stock reservation, reacts to payment, advances the order." "MassTransit saga" {
                    tags "Service"
                }
                consumers = container "Payment consumers" "PaymentSucceededConsumer / PaymentFailedConsumer turn payment integration events into order status changes." "MassTransit consumers" {
                    tags "Service"
                }
            }

            // ── The core: no outward dependencies ──────────────────────────────
            group "Domain" {
                order = container "Order aggregate" "Aggregate root. Owns the allowed-transition state machine (_allowedTransitions / UpdateStatus) and raises domain events. Zero infrastructure dependencies." "Domain model" {
                    tags "Service"
                }
            }

            // ── Persistence and messaging plumbing ─────────────────────────────
            group "Infrastructure" {
                repository = container "Order repository + specifications" "Implements IOrderRepository with the specification pattern to express queries." "EF Core repository" {
                    tags "Service"
                }
                uow = container "Unit of work" "Commits the aggregate and its outbox messages together." "EF Core" {
                    tags "Service"
                }
                dbcontext = container "OrderDbContext" "EF Core fluent mapping to PostgreSQL. The domain carries no EF attributes." "EF Core 9 · Npgsql" {
                    tags "Service"
                }
                outbox = container "Transactional outbox" "MassTransit EF outbox: a state change and its integration event are written atomically, then dispatched." "MassTransit outbox" {
                    tags "Service"
                }
            }
        }

        // ── Dependencies point INWARD toward the Domain (the Clean Architecture rule) ──
        antkart.exceptions -> antkart.endpoints "Wraps the request pipeline"
        antkart.endpoints -> antkart.mediatr "Sends commands / queries"
        antkart.mediatr -> antkart.commands "Validated commands"
        antkart.mediatr -> antkart.queries "Queries"
        antkart.commands -> antkart.order "Enforces invariants on"
        antkart.queries -> antkart.order "Reads"
        antkart.saga -> antkart.order "Advances the state of"
        antkart.consumers -> antkart.order "Applies payment outcome to"
        antkart.repository -> antkart.order "Loads & persists"
        antkart.uow -> antkart.repository "Commits through"
        antkart.dbcontext -> antkart.order "Maps to the database"
        antkart.outbox -> antkart.dbcontext "Shares the same transaction as"
    }

    views {

        themes https://static.structurizr.com/themes/microsoft-azure-2021.01.26/theme.json

        container antkart "PlatformArchitecture" "How is the code built? Clean Architecture layers, dependencies pointing inward." {
            include *
            // PHASE ONE: autoLayout is ON. Before hand-arranging, COMMENT OUT the next
            // line, refresh, then drag. Leave it commented once you start dragging.
            autoLayout lr 300 150
        }

        styles {
            // Hide descriptions. Keep metadata — that is the technology line.
            element "Element" {
                description true
                metadata true
            }
            element "Group" {
                strokeWidth 4
                color #5F5E5A
                fontSize 24
            }
            element "Person" {
                shape Person
                background #5F5E5A
                color #ffffff
                fontSize 26
            }
            element "Software System" {
                background #888780
                color #ffffff
            }
            element "External" {
                background #888780
                color #ffffff
            }
            element "Identity" {
                background #BA7517
                color #ffffff
            }
            element "Managed" {
                background #378ADD
                color #ffffff
            }
            element "Service" {
                background #1D9E75
                color #ffffff
                shape RoundedBox
            }
            element "Serverless" {
                background #0F6E56
                color #ffffff
                shape RoundedBox
            }
            // Additions for the seven layer diagrams. These style tags hero-system
            // never uses, so the shared look of the set is unchanged; they only give
            // the new concepts (data stores, edge, CI/CD, planned, known gaps) a
            // consistent appearance across the seven.
            element "Datastore" {
                background #185FA5
                color #ffffff
                shape Cylinder
            }
            element "Edge" {
                background #7F77DD
                color #ffffff
                shape RoundedBox
            }
            element "CICD" {
                background #639922
                color #ffffff
                shape RoundedBox
            }
            element "Infra" {
                background #888780
                color #ffffff
            }
            element "Planned" {
                background #D9D8D4
                color #5F5E5A
                border dashed
                strokeWidth 3
            }
            element "Issue" {
                background #ffffff
                color #E24B4A
                stroke #E24B4A
                strokeWidth 4
                border dashed
            }
            relationship "Relationship" {
                dashed false
                thickness 2
                fontSize 22
                routing Orthogonal
            }
            relationship "Planned" {
                dashed true
                thickness 2
                fontSize 22
                routing Orthogonal
            }
        }
    }
}
