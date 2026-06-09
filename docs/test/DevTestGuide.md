# AntKart — Developer Manual Test Guide

This guide walks a developer through manually testing every AntKart service end-to-end using Postman. It starts from a clean Docker environment and progresses through each service in dependency order, covering positive flows, negative flows, compensation scenarios, RabbitMQ event monitoring, Kibana log tracing, circuit breaker testing, and email notification verification.

**Intended audience:** Developers and reviewers running a full end-to-end validation of the platform.  
**Time required:** 3–4 hours for a complete run-through.

> **Scope note:** This guide covers **Phase-1 local running** of the AntKart platform via Docker Compose, using the public **AntKart (Phase 1) microservices repository** ([`AntKart`](https://github.com/seesathish/AntKart)), which it clones in Section 2. The cloud-native repository (Phase 2) targets **cloud deployment** — services run locally against live cloud services or are debugged via cloud port-forwarding, with no local docker-compose stack of its own. The `docker-compose` commands below operate on the cloned Phase 1 repository.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Start the Environment](#2-start-the-environment)
3. [Import the Postman Collection](#3-import-the-postman-collection)
4. [Keycloak Setup — Users and Roles](#4-keycloak-setup--users-and-roles)
5. [Razorpay Test Setup](#5-razorpay-test-setup)
6. [Notification Email Setup (Mailhog)](#6-notification-email-setup-mailhog)
7. [Test: Products Service](#7-test-products-service)
8. [Test: Discount Service (gRPC)](#8-test-discount-service-grpc)
9. [Test: Shopping Cart Service](#9-test-shopping-cart-service)
10. [Test: Order Service + SAGA](#10-test-order-service--saga)
11. [Monitor: RabbitMQ Event Flow](#11-monitor-rabbitmq-event-flow)
12. [Monitor: Kibana Logs + Correlation ID Tracing](#12-monitor-kibana-logs--correlation-id-tracing)
13. [Test: Payments Service](#13-test-payments-service)
14. [Verify: Notification Emails](#14-verify-notification-emails)
15. [Test: Circuit Breaker](#15-test-circuit-breaker)
16. [Test: SAGA Compensation Scenarios](#16-test-saga-compensation-scenarios)
17. [Test: Negative Scenarios (Auth + Validation)](#17-test-negative-scenarios-auth--validation)
18. [Full End-to-End Flow Summary](#18-full-end-to-end-flow-summary)

---

## 1. Prerequisites

Install the following tools before starting. All are free.

| Tool | Version | Download |
|------|---------|----------|
| Docker Desktop | Latest | https://www.docker.com/products/docker-desktop |
| Postman | Latest | https://www.postman.com/downloads |
| Git | Any | https://git-scm.com |
| A web browser | Chrome / Edge | — |

**Verify Docker is running:**  
Open a terminal and run:
```bash
docker --version
docker-compose --version
```
Both commands must print a version number. If Docker Desktop is not running, start it from your applications menu and wait for the whale icon in the system tray to become steady (not animated).

---

## 2. Start the Environment

### 2.1 Clone and Navigate

```bash
git clone https://github.com/seesathish/AntKart.git
cd AntKart
```

### 2.2 Start All Services

```bash
docker-compose up -d
```

This command downloads images and starts **16 containers**. The first run takes 5–10 minutes depending on your internet speed. Subsequent runs take about 60 seconds.

### 2.3 Verify All Containers Are Running

```bash
docker ps --format "table {{.Names}}\t{{.Status}}"
```

You should see all 16 containers with status `Up` or `Up (healthy)`. The table should look like this:

```
NAMES                      STATUS
antkart-gateway-api        Up
antkart-products-api       Up
antkart-shoppingcart-api   Up
antkart-order-api          Up
antkart-notification-api   Up
antkart-payments-api       Up
antkart-useridentity-api   Up
antkart-discount-grpc      Up
antkart-kibana             Up
antkart-rabbitmq           Up (healthy)
antkart-postgres           Up (healthy)
antkart-redis              Up (healthy)
antkart-keycloak           Up (healthy)
antkart-mailhog            Up
antkart-mongodb            Up (healthy)
antkart-elasticsearch      Up (healthy)
```

> **If a container shows `Restarting` or `Exited`:** Run `docker logs antkart-<service-name>` to see the error. Common causes: another app is using the same port, or Docker doesn't have enough memory (set at least 4 GB in Docker Desktop → Settings → Resources).

### 2.4 Service URLs — Bookmark These

Open each URL in your browser to confirm it responds:

| Service | URL | Expected |
|---------|-----|----------|
| API Gateway | http://localhost:8000/health | `Healthy` |
| Keycloak Admin | http://localhost:8090 | Login page |
| RabbitMQ Management | http://localhost:15672 | Login page |
| Mailhog (email inbox) | http://localhost:8025 | Empty inbox |
| Kibana (logs) | http://localhost:5601 | Kibana home |
| Products Swagger | http://localhost:5077/swagger | Swagger UI |
| Orders Swagger | http://localhost:5080/swagger | Swagger UI |
| Payments Swagger | http://localhost:5086/swagger | Swagger UI |
| Notification Swagger | http://localhost:5087/swagger | Swagger UI |
| Identity Swagger | http://localhost:5085/swagger | Swagger UI |

> **Note:** The Swagger UIs are only available in Development mode. When testing through Docker, the services run in Production mode — use the API Gateway at port 8000 or the direct service ports.

---

## 3. Import the Postman Collection

### 3.1 Open Postman

Launch Postman. If it asks you to sign in, you can skip and use it without an account by clicking **"Skip signing in and take me straight to the app"**.

### 3.2 Import the Collection File

1. In Postman, click the **Import** button at the top left (next to the "New" button).
2. Click **"Upload Files"**.
3. Navigate to your cloned repository folder.
4. Select the file `AntKart.postman_collection.json`.
5. Click **Open**, then click **Import**.

You will see a new collection called **"AntKart"** appear in the left sidebar with folders for each service.

### 3.3 Understand the Collection Variables

The collection uses variables so you don't have to type URLs repeatedly. Click the **AntKart** collection name in the sidebar, then click the **Variables** tab.

You will see these pre-configured variables:

| Variable | Value | What it points to |
|----------|-------|-------------------|
| `productsUrl` | `http://localhost:5077` | Products service (dev) |
| `cartUrl` | `http://localhost:5079` | Shopping Cart service (dev) |
| `orderUrl` | `http://localhost:5080` | Order service (dev) |
| `identityUrl` | `http://localhost:5085` | UserIdentity service (dev) |
| `paymentsUrl` | `http://localhost:5086` | Payments service (dev) |
| `notificationUrl` | `http://localhost:5087` | Notification service (dev) |
| `gatewayUrl` | `http://localhost:8000` | API Gateway |
| `keycloakUrl` | `http://localhost:8090` | Keycloak |
| `accessToken` | _(empty)_ | JWT token — you will fill this after login |
| `productId` | _(empty)_ | Product ID — fill when you create/get a product |
| `orderId` | _(empty)_ | Order ID — fill when you create an order |

> **Important:** These variables point to the **local dev ports**, not Docker ports. The services must be running either via `docker-compose` (which exposes these ports) or via `dotnet run` locally. Both work.

### 3.4 Set the Authorization Header

Most requests need a JWT Bearer token. Rather than setting it on every request, it is configured at the collection level.

1. Click the **AntKart** collection name.
2. Click the **Authorization** tab.
3. You will see **Type: Bearer Token** with `{{accessToken}}` as the token value.
4. This means every request in the collection automatically sends `Authorization: Bearer <your-token>`.
5. You only need to update the `accessToken` variable after logging in — explained in Section 4.

---

## 4. Keycloak Setup — Users and Roles

Keycloak is the identity provider. All login, registration, and role assignment goes through Keycloak. The Docker setup automatically creates the `antkart` realm and the `antkart-client` application.

### 4.1 Log In to Keycloak Admin Portal

1. Open http://localhost:8090 in your browser.
2. Click **"Administration Console"**.
3. Enter:
   - **Username:** `admin`
   - **Password:** `admin`
4. Click **Sign In**.

You are now in the Keycloak admin dashboard.

### 4.2 Switch to the AntKart Realm

1. At the top left, you will see a dropdown that says **"master"**.
2. Click it and select **"antkart"**.

You are now managing the AntKart realm where all application users live.

### 4.3 View Existing Realm Roles

1. Click **"Realm roles"** in the left sidebar.
2. You should see two roles: **`user`** and **`admin`**.

These roles are already created. Every registered user automatically gets the `user` role. Admin access must be assigned manually.

### 4.4 Register a Regular Test User via Postman

In Postman, open the folder **AK.UserIdentity API → Auth** and click **POST Register**.

1. Click the **Body** tab. You will see:
```json
{
  "username": "testuser1",
  "email": "testuser1@example.com",
  "password": "Test@1234",
  "firstName": "Test",
  "lastName": "User1"
}
```
2. Click **Send**.
3. Expected response:
```json
{
  "message": "User registered successfully."
}
```

> **If you get a 409 Conflict:** The username already exists. Change the username to something unique like `testuser_jan` and try again.

### 4.5 Register a Second Test User

Send the POST Register request again with different details:
```json
{
  "username": "testuser2",
  "email": "testuser2@example.com",
  "password": "Test@1234",
  "firstName": "Test",
  "lastName": "User2"
}
```

### 4.6 Log In as testuser1 and Get a Token

1. Open **POST Login (User)** in Postman.
2. The body is:
```json
{
  "username": "testuser1",
  "password": "Test@1234"
}
```
3. Click **Send**.
4. You will get a response like:
```json
{
  "accessToken": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9...",
  "expiresIn": 300
}
```
5. Copy the entire `accessToken` value (the long string starting with `eyJ`).
6. In Postman, click the **AntKart** collection name → **Variables** tab.
7. Find the `accessToken` row and paste the copied token into the **Current Value** column.
8. Click **Save** (Ctrl+S).

> **Tokens expire after 5 minutes.** If you get a 401 Unauthorized response during testing, just log in again and update the `accessToken` variable.

### 4.7 Verify the Token Works

Click **GET Me (current user)** in Postman and click **Send**.

Expected response:
```json
{
  "id": "...",
  "username": "testuser1",
  "email": "testuser1@example.com",
  "firstName": "Test",
  "lastName": "User1",
  "roles": ["user"]
}
```

### 4.8 Create an Admin User

1. Register a new user called `adminuser` using POST Register:
```json
{
  "username": "adminuser",
  "email": "admin@example.com",
  "password": "Admin@1234",
  "firstName": "Admin",
  "lastName": "User"
}
```

2. Now assign the `admin` role. In Keycloak Admin Portal:
   - Click **Users** in the left sidebar.
   - Search for `adminuser` and click on it.
   - Click the **Role mapping** tab.
   - Click **Assign role**.
   - Search for `admin`, tick the checkbox next to it, click **Assign**.

3. Alternatively, use the Postman API. First log in as the Keycloak admin (not the app admin):
   - Open **POST Login (Admin)** and send it.
   - Copy the `accessToken` and update the collection variable.
   - Then open **POST Assign Role (admin only)** — update the user ID in the URL with the admin user's Keycloak ID and send.

4. Log in as `adminuser`:
```json
{
  "username": "adminuser",
  "password": "Admin@1234"
}
```
5. Call **GET Me** — you should see `"roles": ["user", "admin"]` in the response.

### 4.9 Check Brute Force Protection

Try logging in with a **wrong password 6 times** in a row. On the 6th attempt, even with the correct password, you should get an error message saying the account is temporarily locked. This confirms brute force protection is active (threshold = 5 attempts).

To unlock the account: In Keycloak Admin → Users → testuser1 → click **"Clear temporary lockout"** button in the Credentials tab.

---

## 5. Razorpay Test Setup

Razorpay is the payment gateway. For testing, you use a **sandbox account** with test keys — no real money is charged.

### 5.1 Get Test API Keys

1. Go to https://dashboard.razorpay.com (create a free account if you don't have one).
2. Make sure you are in **Test Mode** — there is a toggle at the top right. It should say **"Test Mode"** highlighted.
3. Go to **Settings → API Keys**.
4. Click **"Generate Test Key"**.
5. You will get a **Key ID** (starts with `rzp_test_`) and a **Key Secret**.
6. Copy both — you need them in the next step.

> **The docker-compose already has default test keys configured.** If you just want to run tests without your own Razorpay account, the default keys in `docker-compose.yml` will work for basic flows but may have limits.

### 5.2 Razorpay Test Cards

When Razorpay presents a payment form, use these test card numbers (no real money deducted):

| Card Type | Number | Expiry | CVV | OTP |
|-----------|--------|--------|-----|-----|
| Visa | 4111 1111 1111 1111 | Any future date | Any 3 digits | 1234 1234 |
| Mastercard | 5267 3169 4984 2643 | Any future date | Any 3 digits | 1234 1234 |

> In AntKart's current API-only testing (no frontend), Razorpay returns test order IDs and you simulate the payment — explained in detail in Section 13.

---

## 6. Notification Email Setup (Mailhog)

Mailhog is a local email trap. Every email that AntKart sends gets caught by Mailhog — it never reaches a real inbox. This lets you test email notifications without configuring real email credentials.

### 6.1 Open Mailhog

1. Open http://localhost:8025 in your browser.
2. You will see an empty inbox. Every email AntKart sends will appear here within a few seconds.
3. Keep this tab open throughout your testing session.

### 6.2 What Triggers an Email

| User Action | Email Sent |
|-------------|------------|
| Register a new user | Welcome email |
| Create an order | Order confirmation email |
| Order confirmed (stock reserved) | Stock confirmed email |
| Order cancelled | Cancellation notice email |
| Payment successful | Payment receipt email |
| Payment failed | Payment failure alert email |

You do not need to do anything special — emails arrive automatically when these events happen during your testing.

---

## 7. Test: Products Service

The Products service is **publicly accessible** — no login required to browse products.

### 7.1 Get All Products (Positive)

In Postman, open **AK.Products API → Products — Read → GET All Products (paged)**.

1. Click **Send**.
2. Expected: Status `200 OK`, response body with a list of products and pagination info:
```json
{
  "items": [
    {
      "id": "abc123...",
      "name": "Men's Classic Shirt",
      "categoryName": "Men",
      "subCategoryName": "Shirts",
      "price": 999.99,
      "stockQuantity": 50,
      "sku": "MEN-SHIR-001"
    }
  ],
  "totalCount": 300,
  "page": 1,
  "pageSize": 20
}
```
3. Copy any `id` value from the response.
4. In Postman Collection Variables, set `productId` to this value.

### 7.2 Filter by Category

Open **GET Products — Filter by Category (Men)** and click Send.

- Expected: Only Men's products in the response.
- Check: All items have `"categoryName": "Men"`.

### 7.3 Get a Product by ID

Open **GET Product by ID**. The URL contains `{{productId}}` which uses the variable you just set.

Click Send. Expected: A single product object with full details.

### 7.4 Search Products

Open **GET Products — Search by Keyword**. The URL has `?search=shirt` (or similar).

Expected: Products whose name or description contains the search term.

### 7.5 Create a Product (Requires Admin Token)

> **Before doing this:** Log in as `adminuser`, copy the token, and update `accessToken`.

Open **POST Create Product (Men — Shirts)** and click Send.

Body:
```json
{
  "name": "Men's Premium Oxford Shirt",
  "description": "A premium quality Oxford shirt for men made with 100% cotton.",
  "sku": "MEN-OXF-NEW-001",
  "brand": "ArrowMen",
  "categoryName": "Men",
  "subCategoryName": "Shirts",
  "price": 1299.99,
  "currency": "INR",
  "stockQuantity": 50,
  "size": "L",
  "color": "White",
  "imageUrls": []
}
```

Expected response: `201 Created` with the new product including its generated `id`.

### 7.6 Negative Scenario — Missing Required Field

Send a POST Create Product request with the `sku` field removed:
```json
{
  "name": "Test Product",
  "price": 99.99,
  "stockQuantity": 10
}
```
Expected: `400 Bad Request` with validation errors listing which fields are missing.

### 7.7 Negative Scenario — Duplicate SKU

Try to create another product with the same SKU `"MEN-OXF-NEW-001"`.

Expected: `409 Conflict` — the SKU already exists.

### 7.8 Negative Scenario — Negative Price

```json
{
  "name": "Bad Product",
  "sku": "BAD-001",
  "price": -50.00,
  "stockQuantity": 5,
  "categoryName": "Men",
  "subCategoryName": "Shirts"
}
```
Expected: `400 Bad Request` — price must be greater than zero.

### 7.9 Check Products Health

Open **GET Products — Health Check** and Send.

Expected: `200 OK` with `{"status": "Healthy"}`.

---

## 8. Test: Discount Service (gRPC)

The Discount service uses gRPC, not REST. Postman supports gRPC natively but needs the proto file.

### 8.1 Set Up gRPC in Postman

1. Click the **New** button (top left) in Postman.
2. Select **gRPC Request**.
3. In the URL field, enter: `localhost:5001`
4. Click **"Select a method"** → click **"Import a .proto file"**.
5. Navigate to `AK.Discount/AK.Discount.Grpc/Protos/discount.proto` in your cloned repo.
6. Click **Open**.

You should now see the available RPCs:
- `GetDiscount`
- `CreateDiscount`
- `UpdateDiscount`
- `DeleteDiscount`
- `GetAllDiscounts`

> **Alternative — use grpcurl from the Postman descriptions:** The Postman collection has grpcurl commands in the description of each Discount request. Open any Discount request and read the **Description** tab — you can copy and run those commands in your terminal.

### 8.2 Get All Discounts

In the gRPC request, select **GetAllDiscounts** from the method dropdown.

Message body:
```json
{}
```
Click **Invoke**. Expected: A list of 300 discount coupons, one per product SKU.

### 8.3 Get Discount by Product ID

Select **GetDiscount**.

Message body (replace with a real SKU from the Products test):
```json
{
  "productId": "MEN-SHIR-001"
}
```
Expected:
```json
{
  "id": 1,
  "productId": "MEN-SHIR-001",
  "description": "10% off Men's Shirts",
  "amount": 100
}
```

### 8.4 Negative Scenario — Non-Existent SKU

```json
{
  "productId": "DOES-NOT-EXIST"
}
```
Expected: gRPC status `NOT_FOUND`.

---

## 9. Test: Shopping Cart Service

The cart requires a logged-in user. All cart operations are automatically scoped to the logged-in user — you cannot access another user's cart.

> **Before starting:** Make sure `accessToken` is set to a valid token for `testuser1`.

### 9.1 View Your Cart (Empty)

Open **AK.ShoppingCart API → GET Cart** and Send.

Expected:
```json
{
  "userId": "...",
  "items": [],
  "totalPrice": 0
}
```

### 9.2 Add an Item to the Cart

Open **POST Add Item to Cart**.

Body (update `productId` with a real product ID from Section 7.1):
```json
{
  "productId": "{{cartProductId}}",
  "productName": "Men's Classic Shirt",
  "sku": "MEN-SHIR-001",
  "price": 999.99,
  "quantity": 2,
  "imageUrl": null
}
```

1. First update the `cartProductId` collection variable to a real product ID from your Products test.
2. Click **Send**.

Expected: `200 OK` — cart updated with the item.

### 9.3 Verify the Cart

Click **GET Cart** again. Expected: Your cart now shows the item with quantity 2 and a total price.

### 9.4 Add the Same Item Again (Quantity Accumulates)

Send **POST Add Item to Cart** again with the same `productId` and `quantity: 1`.

Expected: The item's quantity should now be 3 (not a duplicate entry).

### 9.5 Update Item Quantity

Open **PUT Update Cart Item Quantity**.

Body:
```json
{
  "quantity": 5
}
```
The URL contains `{{cartProductId}}` as the product ID.

Click **Send**. Expected: `200 OK`.

Verify by calling **GET Cart** — quantity should now be 5.

### 9.6 Remove an Item

Open **DELETE Remove Item from Cart**.

Click **Send**. Expected: `200 OK`.

Call **GET Cart** again — the item should be gone.

### 9.7 Add Multiple Items

Add at least 2 different products to your cart so you can create an order in the next section. Use the product IDs you found in Section 7.

### 9.8 Negative Scenario — No Token

Open **GET Cart** and temporarily clear the `accessToken` collection variable.

Click **Send**. Expected: `401 Unauthorized`.

Restore the token.

### 9.9 Negative Scenario — Zero Quantity

```json
{
  "productId": "some-id",
  "productName": "Test",
  "sku": "TEST-001",
  "price": 100.00,
  "quantity": 0
}
```
Expected: `400 Bad Request` — quantity must be at least 1.

---

## 10. Test: Order Service + SAGA

Creating an order triggers the SAGA (distributed transaction) — one of the most important flows in the system. When you create an order:

1. Order is saved with `Pending` status
2. `OrderCreatedIntegrationEvent` is published to RabbitMQ
3. The SAGA orchestrator picks it up and sends a stock reservation request to Products
4. Products service reserves stock and publishes `StockReservedIntegrationEvent`
5. SAGA receives this and publishes `OrderConfirmedIntegrationEvent`
6. Order status changes to `Confirmed`
7. Notification service sends an email

You will watch this entire flow in RabbitMQ (Section 11) and Kibana (Section 12).

### 10.1 Make Sure Your Cart Has Items

Call **GET Cart** to confirm you have at least 1 item in the cart. If the cart is empty, add items as per Section 9.7.

Note down the `productId`, `productName`, `sku`, and `price` from your cart items — you need these for the order body.

### 10.2 Create an Order

Open **AK.Order API → POST Create Order**.

Body:
```json
{
  "shippingAddress": {
    "fullName": "Test User1",
    "addressLine1": "123 Main Street",
    "addressLine2": "Apartment 4B",
    "city": "Chennai",
    "state": "Tamil Nadu",
    "postalCode": "600001",
    "country": "India",
    "phone": "+919876543210"
  },
  "items": [
    {
      "productId": "PASTE-YOUR-PRODUCT-ID-HERE",
      "productName": "Men's Classic Shirt",
      "sku": "MEN-SHIR-001",
      "quantity": 2,
      "unitPrice": 999.99,
      "imageUrl": null
    }
  ],
  "notes": "Please deliver before 6 PM"
}
```

Replace `PASTE-YOUR-PRODUCT-ID-HERE` with a real product ID.

Click **Send**.

Expected: `201 Created` response:
```json
{
  "id": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "orderNumber": "ORD-20260517-ABCD1234",
  "userId": "...",
  "status": "Pending",
  "totalAmount": 1999.98,
  "items": [...],
  "shippingAddress": {...},
  "createdAt": "2026-05-17T..."
}
```

**Copy the `id` value** and save it in the Postman collection variable `orderId`.  
**Copy the `orderNumber`** (e.g. `ORD-20260517-ABCD1234`) — you need it for payment.

### 10.3 Get Your Orders

Open **GET My Orders** and click Send.

Expected: A paginated list containing the order you just created with status `Pending` or `Confirmed` (it may have transitioned already via SAGA).

### 10.4 Get Order by ID

Open **GET Order by ID**. It uses `{{orderId}}` from your variable.

Click Send. Expected: The full order details.

Note the `status` field — watch it change as the SAGA progresses:
- `Pending` → right after creation
- `Confirmed` → after stock is reserved
- `Processing` / `Shipped` → after status update (admin action)
- `Delivered` → terminal state

### 10.5 Watch the SAGA in Action

Immediately after creating the order, switch to your browser and:
1. Open RabbitMQ Management: http://localhost:15672 (login: `guest` / `guest`)
2. Click **"Queues and Streams"** tab
3. Look for queues starting with `order-`, `products-`, `notification-`

You should see message activity (numbers in the "Ready" and "Unacked" columns briefly appear and then go to 0 as messages are processed). A detailed walkthrough is in Section 11.

### 10.6 Negative Scenario — Order with No Items

```json
{
  "shippingAddress": {
    "fullName": "Test",
    "addressLine1": "123 St",
    "city": "Chennai",
    "state": "Tamil Nadu",
    "postalCode": "600001",
    "country": "India",
    "phone": "+919876543210"
  },
  "items": []
}
```
Expected: `400 Bad Request` — order must have at least one item.

### 10.7 Negative Scenario — Invalid Postal Code

```json
{
  "shippingAddress": {
    "fullName": "Test",
    "addressLine1": "123 St",
    "city": "Chennai",
    "state": "Tamil Nadu",
    "postalCode": "INVALID",
    "country": "India",
    "phone": "+919876543210"
  },
  "items": [...]
}
```
Expected: `400 Bad Request` — validation error on postalCode.

### 10.8 Cross-User IDOR Test

1. Log in as `testuser2` and update `accessToken`.
2. Try to get `testuser1`'s order: **GET Order by ID** using `testuser1`'s `orderId`.
3. Expected: `403 Forbidden` — you cannot access another user's order.
4. Log back in as `testuser1` and restore the token.

---

## 11. Monitor: RabbitMQ Event Flow

RabbitMQ is the message broker. Every integration event flows through it. This section shows you how to watch events in real time.

### 11.1 Open RabbitMQ Management

1. Open http://localhost:15672
2. Username: `guest`, Password: `guest`
3. Click **"Queues and Streams"** tab.

### 11.2 Understand the Queue Naming Pattern

Each queue is named `{service-prefix}-{event-type}`. For example:
- `notification-order-created` — the Notification service's queue for `OrderCreatedIntegrationEvent`
- `order-payment-succeeded` — the Order service's queue for `PaymentSucceededIntegrationEvent`

Multiple services can receive the same event independently (fan-out pattern — not competing consumers).

### 11.3 Watch the Order Creation Flow

1. Keep the RabbitMQ Queues page open in your browser.
2. In Postman, create a new order (Section 10.2).
3. Immediately refresh the RabbitMQ Queues page.
4. You should briefly see message counts in these queues:

| Queue | Event | Who processes it |
|-------|-------|-----------------|
| `notification-order-created` | OrderCreatedIntegrationEvent | Notification → sends Order Confirmation email |
| `products-order-created` | OrderCreatedIntegrationEvent | Products → checks/reserves stock |

5. Within a few seconds, the counts drop to 0 (messages consumed).
6. After stock reservation, watch for:

| Queue | Event | Who processes it |
|-------|-------|-----------------|
| `notification-order-confirmed` | OrderConfirmedIntegrationEvent | Notification → sends "Stock Confirmed" email |
| `cart-order-confirmed` | OrderConfirmedIntegrationEvent | Cart → clears the user's cart |

### 11.4 Watch a Specific Exchange

1. Click the **"Exchanges"** tab.
2. Find the exchange `order-created` (or similar naming).
3. Click on it.
4. Under **"Bindings"**, you can see which queues are bound to receive this event.

### 11.5 Monitor a Queue in Detail

1. Click **"Queues and Streams"**.
2. Click on the queue `notification-order-created`.
3. Click **"Get messages"** at the bottom.
4. Set Count to 1, click **Get Message(s)**.
5. You can see the raw JSON message payload that was published.
6. You will see fields like `OrderId`, `OrderNumber`, `CustomerEmail`, `CustomerName` — all the data the Notification service needs to send the email.

> **Note:** Getting a message this way is a **peek** — it does not consume the message. The service consumer still processes it.

### 11.6 Check the SAGA State (Advanced)

The SAGA orchestrator state is stored in PostgreSQL. To check it:

```bash
docker exec -it antkart-postgres psql -U antkart -d AKOrdersDb \
  -c "SELECT \"CurrentState\", \"OrderId\", \"CreatedAt\" FROM \"OrderSagaStates\" ORDER BY \"CreatedAt\" DESC LIMIT 5;"
```

You should see rows with states like `StockPending`, `Confirmed`, or `Cancelled`.

### 11.7 Dead-Letter Queue — What to Look For

If a consumer fails repeatedly, messages go to a dead-letter queue. Check for any queues with `_error` or `_skipped` suffix. In a healthy system, these should be empty.

```bash
# Check for dead-letter queues with messages
curl -s -u guest:guest http://localhost:15672/api/queues | \
  python3 -c "
import sys, json
qs = json.load(sys.stdin)
for q in qs:
    if q.get('messages', 0) > 0 and ('error' in q['name'] or 'skipped' in q['name']):
        print(q['name'], '-', q['messages'], 'messages')
print('Dead-letter check complete')
"
```

---

## 12. Monitor: Kibana Logs + Correlation ID Tracing

Every HTTP request through the Gateway gets a unique `X-Correlation-Id` header. This ID appears in every log line from every service that handled that request — making it possible to trace a single request across all microservices.

### 12.1 Open Kibana

1. Open http://localhost:5601 in your browser.
2. If prompted with "Try our sample data" or a welcome screen, close it or click **"Explore on my own"**.

### 12.2 Create a Data View (First Time Only)

You only need to do this once. If you already see log data, skip to 12.4.

1. Click the **hamburger menu** (three lines, top left).
2. Under **"Management"**, click **"Stack Management"**.
3. Click **"Data Views"**.
4. Click **"Create data view"**.
5. Fill in:
   - **Name:** `AntKart Logs`
   - **Index pattern:** `antkart-logs-*`
   - **Timestamp field:** `@timestamp`
6. Click **"Save data view to Kibana"**.

### 12.3 Open Discover

1. Click the hamburger menu again.
2. Under **"Analytics"**, click **"Discover"**.
3. In the top left, select the **"AntKart Logs"** data view from the dropdown.
4. Set the time range to **"Last 15 minutes"** (top right time picker).
5. Click **Refresh**.

You should now see log entries from all AntKart services.

### 12.4 Add Useful Columns

By default, Kibana shows the full log line. Add these columns for a cleaner view:

1. In the left sidebar under "Available fields", search for `service` and click the **+** button next to it.
2. Also add: `level`, `message`, `CorrelationId`, `requestPath`.

Now each log row shows: timestamp, service name, log level, message, and correlation ID.

### 12.5 Capture a Correlation ID

When you make a request in Postman:
1. Look at the **response headers** (click the **"Headers"** tab in the Postman response panel).
2. Find the header `X-Correlation-Id`. Copy its value (a UUID like `a1b2c3d4-...`).

> **Tip:** Every response from the Gateway has this header. It is the same ID that will appear in logs from all services that handled the request.

### 12.6 Trace a Single Request in Kibana

1. In Kibana Discover, click the **"Add filter"** button (the **+** icon near the search bar).
2. Field: `CorrelationId`
3. Operator: `is`
4. Value: paste the correlation ID you copied from Postman
5. Click **Save**.

Now Kibana shows **only** the log lines related to your specific request — across all services.

You will see entries from:
- Gateway (request received, forwarded)
- The downstream service (handler executed, DB query, event published)
- Notification service (event consumed, email sent)

This is how you debug a request that behaves differently than expected.

### 12.7 Sample KQL Queries

In the search bar at the top of Kibana Discover, try these queries:

```kql
# All errors across all services
level: "Error"

# All logs from the Order service only
service: "AK.Order.API"

# All logs from a specific correlation ID
CorrelationId: "paste-id-here"

# SAGA-related logs
message: "saga" or message: "Saga"

# Payment-related logs
service: "AK.Payments.API"

# All 500 errors
statusCode: 500
```

---

## 13. Test: Payments Service

The payment flow has three steps:
1. **Initiate** — create a Razorpay order
2. **Simulate payment** — in a real app, the user enters card details in Razorpay's UI; in API testing, we use Razorpay's test API to simulate
3. **Verify** — confirm the payment signature matches

### 13.1 Initiate a Payment

Open **AK.Payments API → POST Initiate Payment**.

Body (update with your real orderId and orderNumber from Section 10.2):
```json
{
  "orderId": "{{orderId}}",
  "orderNumber": "ORD-20260517-ABCD1234",
  "amount": 199998,
  "method": 1
}
```

> **Amount** is in paise (INR smallest unit). 199998 paise = ₹1,999.98. Match this to your actual order total.  
> **method: 1** means Card. Other values: 2 = UPI, 3 = NetBanking.

Click **Send**.

Expected response:
```json
{
  "paymentId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "razorpayOrderId": "order_XXXXXXXXXXX",
  "razorpayKeyId": "rzp_test_XXXXXXXXXX",
  "amount": 199998,
  "currency": "INR"
}
```

Save these values in Postman collection variables:
- `paymentId` → the AntKart internal payment UUID
- `razorpayOrderId` → the `razorpayOrderId` from the response

### 13.2 Simulate the Payment (Get razorpayPaymentId and Signature)

In a real application, the user fills a Razorpay payment form and Razorpay sends back a `razorpayPaymentId` and `razorpaySignature`. For API testing, you simulate this:

**Option A — Use Razorpay Test Dashboard:**
1. Go to https://dashboard.razorpay.com (test mode).
2. Navigate to **Payments** → find your order by the `razorpayOrderId`.
3. Click on it and use the "Simulate Payment" option to get test payment IDs.

**Option B — Use Razorpay API directly (curl):**
```bash
curl -X POST https://api.razorpay.com/v1/payments \
  -u "rzp_test_YOUR_KEY_ID:YOUR_KEY_SECRET" \
  -d "amount=199998&currency=INR&method=card&card[number]=4111111111111111&card[expiry_month]=12&card[expiry_year]=2026&card[cvv]=123&order_id=order_XXXXXXXXXX"
```

This gives you back a `payment_id`.

**For the signature**, compute HMAC-SHA256:
- Message: `{razorpayOrderId}|{razorpayPaymentId}`
- Key: Your Razorpay Key Secret

In Python:
```python
import hmac, hashlib
key_secret = "YOUR_KEY_SECRET"
message = "order_XXXXX|pay_YYYYY"
sig = hmac.new(key_secret.encode(), message.encode(), hashlib.sha256).hexdigest()
print(sig)
```

### 13.3 Verify the Payment

Open **POST Verify Payment**.

Body:
```json
{
  "paymentId": "{{paymentId}}",
  "razorpayOrderId": "{{razorpayOrderId}}",
  "razorpayPaymentId": "pay_XXXXXXXXXX",
  "razorpaySignature": "the-hmac-signature-you-computed"
}
```

Save `razorpayPaymentId` to the collection variable.

Click **Send**.

Expected: `200 OK` — payment verified. The order status in the Order service will update to reflect payment success.

### 13.4 Check Your Payment History

Open **GET My Payments** and click Send.

Expected: A list of your payments including the one just verified.

### 13.5 IDOR Test — Access Another User's Payments

1. Log in as `testuser2`, update `accessToken`.
2. Call **GET My Payments** — expected: empty list (testuser2 has no payments).
3. Call **GET Payment by ID** with `testuser1`'s `paymentId` — expected: `403 Forbidden`.
4. Log back in as `testuser1`.

### 13.6 Negative Scenario — No Auth

Use the pre-built negative request **[401] Initiate Payment — No Auth** in the collection.

Expected: `401 Unauthorized`.

### 13.7 Negative Scenario — Invalid Amount

Use **[400] Initiate Payment — Missing Amount** and **[400] Initiate Payment — Zero Amount**.

Expected: `400 Bad Request` with validation error message.

### 13.8 Negative Scenario — Invalid Signature

Use **[400] Verify Payment — Invalid Signature**.

Expected: `400 Bad Request` — signature mismatch.

### 13.9 Save a Card

Open **POST Save Card**.

Body:
```json
{
  "razorpayCustomerId": "cust_XXXXXXXXXX",
  "razorpayPaymentId": "{{razorpayPaymentId}}",
  "customerName": "Test User1",
  "customerEmail": "testuser1@example.com",
  "customerContact": "+919876543210"
}
```

> Note: AntKart saves only Razorpay token IDs — no raw card numbers are ever stored (PCI compliance).

Expected: `201 Created` with a `cardId`. Save this to the `cardId` collection variable.

### 13.10 Get Saved Cards

Open **GET Saved Cards** and Send.

Expected: Your saved card listed.

### 13.11 Delete a Saved Card

Open **DELETE Saved Card** and Send.

Expected: `204 No Content`. Call **GET Saved Cards** to confirm it is gone.

---

## 14. Verify: Notification Emails

Every key business event sends an email. Let's verify each one arrived in Mailhog.

### 14.1 Open Mailhog

1. Go to http://localhost:8025.
2. You should see emails that arrived from your testing so far.

### 14.2 Check Welcome Email (User Registration)

1. Look for an email with Subject: **"Welcome to AntKart"** (or similar).
2. The recipient should be the email address you used when registering (`testuser1@example.com`).
3. Click on the email to read it.
4. It should contain a greeting with the user's first name.

> If the email did not arrive, wait 10–15 seconds and refresh Mailhog. If still missing, check the Notification service logs in Kibana: `service: "AK.Notification.API"`.

### 14.3 Check Order Confirmation Email

1. Look for **"Order Confirmation"** or **"Your order has been placed"** subject.
2. It should contain the order number (e.g. `ORD-20260517-ABCD1234`) and item details.
3. The email is sent when `OrderCreatedIntegrationEvent` is consumed by Notification service.

### 14.4 Check Stock Confirmed Email

1. Look for **"Order Confirmed"** or **"Your order has been confirmed"** subject.
2. This email is sent after the SAGA successfully reserves stock (`OrderConfirmedIntegrationEvent`).
3. It arrives a few seconds after the order confirmation email.

### 14.5 Check Payment Receipt Email

1. After completing payment verification (Section 13.3), look for a **"Payment Successful"** email.
2. It should list the amount paid and the order number.

### 14.6 Test Cancellation Email

1. Cancel an order using **DELETE Cancel Order** in Postman (ensure it is in a `Pending` or `Confirmed` state — `Shipped` and `Delivered` orders cannot be cancelled).
2. Check Mailhog for an **"Order Cancelled"** email.

### 14.7 Check the Notification List in Postman

Open **AK.Notification API → GET My Notifications** and Send.

Expected: A list of all notifications that were sent to the logged-in user. Each entry has:
- `type` — the notification type (OrderCreated, PaymentSucceeded, etc.)
- `subject` — email subject
- `sentAt` — when it was sent
- `status` — Sent / Failed

### 14.8 For Real Email (Gmail) — Optional

If you want to test with a real Gmail account instead of Mailhog:

1. Get a Gmail App Password:
   - Go to your Gmail account → Settings → Security → 2-Step Verification → App Passwords.
   - Create an app password for "Mail". Copy the 16-character password.

2. Create a file `docker-compose.gmail.yml` (this file is gitignored — your credentials stay local):
```yaml
services:
  ak-notification-api:
    environment:
      - EmailSettings__Host=smtp.gmail.com
      - EmailSettings__Port=587
      - EmailSettings__Username=your-email@gmail.com
      - EmailSettings__Password=your-app-password
      - EmailSettings__FromEmail=your-email@gmail.com
      - EmailSettings__FromName=AntKart
```

3. Start with the override:
```bash
docker-compose -f docker-compose.yml -f docker-compose.gmail.yml up -d ak-notification-api
```

4. Now trigger any user action and check your real Gmail inbox.

---

## 15. Test: Circuit Breaker

The circuit breaker protects services from cascading failures. If a downstream service fails repeatedly, the circuit "opens" and subsequent requests fail fast (without waiting for timeout) with a 503 response.

The circuit breaker is configured in `ocelot.json`:
- **ExceptionsAllowedBeforeBreaking:** 5 (after 5 errors, circuit opens)
- **DurationOfBreak:** 30000 ms (circuit stays open for 30 seconds)
- **TimeoutValue:** 10000 ms (each request times out after 10 seconds)

### 15.1 Simulate a Service Failure

Stop the Products service container to simulate it going down:

```bash
docker stop antkart-products-api
```

### 15.2 Test Normal Failure (Circuit Closed)

Send **GET All Products** in Postman (through the Gateway at `{{gatewayUrl}}/gateway/products`).

- First few requests: `503 Service Unavailable` or timeout (circuit is closed but the downstream is down)
- After 5 failed requests: The circuit **opens** — subsequent requests immediately return `503` without waiting for the timeout.

### 15.3 Test Circuit Open State

Send 5+ more GET requests rapidly. You should get immediate `503` responses — much faster than the timeout duration. This confirms the circuit is open and protecting downstream services.

### 15.4 Check the Circuit in Gateway Logs

```bash
docker logs antkart-gateway-api 2>&1 | grep -i "circuit\|break\|open\|polly" | tail -20
```

You should see log lines mentioning Polly circuit breaker state transitions.

### 15.5 Recovery — Restart the Service

```bash
docker start antkart-products-api
```

Wait 30 seconds (the circuit's break duration). Then send another GET request.

- The circuit **half-opens** — allows one test request through.
- If that request succeeds, the circuit **closes** and normal operation resumes.
- Subsequent requests return `200 OK`.

### 15.6 Test Timeout

With the service running, you can test timeouts by checking the `TimeoutValue` (10 seconds). If the Products service takes longer than 10 seconds to respond, Ocelot returns `503` even if the service eventually responds.

---

## 16. Test: SAGA Compensation Scenarios

The SAGA pattern handles failures gracefully. When stock reservation fails, the SAGA automatically cancels the order and notifies the customer.

### 16.1 Scenario 1 — Stock Reservation Failure (Happy Path SAGA Compensation)

This tests what happens when a product is **out of stock**.

**Setup — Create a product with 0 stock:**

1. Log in as `adminuser`.
2. Create a product with `stockQuantity: 0`:
```json
{
  "name": "Out of Stock Shirt",
  "sku": "OOS-TEST-001",
  "brand": "TestBrand",
  "categoryName": "Men",
  "subCategoryName": "Shirts",
  "price": 499.99,
  "currency": "INR",
  "stockQuantity": 0
}
```
3. Note the `id` of this product.

**Trigger the SAGA:**

1. Log in as `testuser1`.
2. Add the out-of-stock product to your cart:
```json
{
  "productId": "THE-OUT-OF-STOCK-PRODUCT-ID",
  "productName": "Out of Stock Shirt",
  "sku": "OOS-TEST-001",
  "price": 499.99,
  "quantity": 1
}
```
3. Create an order containing this product.

**Watch the SAGA compensation:**

1. In RabbitMQ, watch for `StockReservationFailedIntegrationEvent` appearing in queues.
2. The SAGA should automatically:
   - Cancel the order (status changes to `Cancelled`)
   - Publish `OrderCancelledIntegrationEvent`
   - Notification service sends a cancellation email

**Verify:**

1. Call **GET Order by ID** — status should be `Cancelled`.
2. Check Mailhog — a cancellation email should have arrived.
3. In Kibana, search for your correlation ID — you should see the full compensation flow in the logs.

### 16.2 Scenario 2 — Payment Failure Compensation

This tests what happens when payment fails after the order is confirmed.

**Setup:**
1. Create a normal order and confirm it goes to `Confirmed` status.
2. Initiate a payment.
3. Verify the payment with an **invalid signature** (wrong signature string).

Expected: Payment fails. The `PaymentFailedIntegrationEvent` is published.

**What the SAGA does:**
- Order service consumes `PaymentFailedIntegrationEvent`
- Order status changes to `PaymentFailed`
- Notification service sends a payment failure email

**Verify:**
1. Call **GET Order by ID** — status should be `PaymentFailed`.
2. Check Mailhog — a payment failure email should have arrived.

### 16.3 Scenario 3 — Order Cancellation by User (Manual Compensation)

1. Create a new order (in `Pending` state).
2. Immediately cancel it using **DELETE Cancel Order**.
3. Expected: `204 No Content`.

**What happens:**
- Order status changes to `Cancelled`
- `OrderCancelledIntegrationEvent` published
- Cancellation email sent
- If stock was reserved, it would be released (in a full implementation)

**Verify:**
1. Call **GET Order by ID** — status = `Cancelled`.
2. Mailhog — cancellation email.

### 16.4 Scenario 4 — Invalid Order Status Transition

The order status follows strict state machine rules:

```
Pending → Confirmed | Cancelled | PaymentFailed
Confirmed → Processing | Shipped | Cancelled
Processing → Shipped | Cancelled
Shipped → Delivered
Delivered → (terminal, no further changes)
Cancelled → (terminal, no further changes)
```

**Test an invalid transition:**

1. Log in as `adminuser`.
2. Create an order that is in `Pending` status.
3. Try to set it directly to `Shipped` (skipping `Confirmed` and `Processing`):

Open **PUT Update Order Status (admin only)** with body:
```json
{
  "status": "Shipped"
}
```

Expected: `409 Conflict` — invalid status transition.

**Test a valid transition:**

With the same order, try: `Pending` → `Confirmed`:
```json
{
  "status": "Confirmed"
}
```
Expected: `200 OK`.

Then `Confirmed` → `Processing`:
```json
{
  "status": "Processing"
}
```
Expected: `200 OK`.

---

## 17. Test: Negative Scenarios (Auth + Validation)

This section covers all security and input validation failures systematically.

### 17.1 Authentication Failures

| Test | How | Expected |
|------|-----|----------|
| No token | Clear `accessToken` variable, call any protected route | `401 Unauthorized` |
| Expired token | Wait 5+ minutes, try calling a protected route | `401 Unauthorized` |
| Tampered token | Modify one character of the token, call a protected route | `401 Unauthorized` |
| Wrong password login | Send POST Login with wrong password | `401` with error message |

### 17.2 Authorization Failures (403)

| Test | How | Expected |
|------|-----|----------|
| User accesses admin endpoint | Log in as `testuser1`, call **GET All Users (admin only)** | `403 Forbidden` |
| User updates order status | Log in as `testuser1`, call **PUT Update Order Status** | `403 Forbidden` |
| User accesses another user's order | Get `testuser1`'s orderId, call GET with `testuser2` token | `403 Forbidden` |
| User accesses admin notifications | Call **GET All Notifications (admin only)** with user token | `403 Forbidden` |

### 17.3 Input Validation Failures (400)

| Test | Payload | Expected Error |
|------|---------|---------------|
| Register with short password | `"password": "abc"` | 400 — password too short |
| Create product with negative price | `"price": -100` | 400 — price must be > 0 |
| Create order with empty items | `"items": []` | 400 — at least one item required |
| Initiate payment with 0 amount | `"amount": 0` | 400 — amount must be > 0 |
| Add to cart with 0 quantity | `"quantity": 0` | 400 — quantity must be >= 1 |

### 17.4 Resource Not Found (404)

| Test | How | Expected |
|------|-----|----------|
| Get non-existent product | Use a fake UUID as productId | `404 Not Found` |
| Get non-existent order | Use a fake UUID as orderId | `404 Not Found` |
| Get non-existent notification | Use a fake UUID | `404 Not Found` |

### 17.5 Rate Limiting (429)

Rate limiting requires sending many concurrent requests. The easiest way is with a shell script:

```bash
# Get a fresh token first
TOKEN=$(curl -s -X POST http://localhost:5085/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser1","password":"Test@1234"}' | \
  python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

# Send 30 concurrent requests to the products endpoint (limit: 20/1s)
tmpdir=$(mktemp -d)
for i in $(seq 1 30); do
  curl -s -o /dev/null -w "%{http_code}" \
    http://localhost:8000/gateway/products \
    -H "Authorization: Bearer $TOKEN" > "$tmpdir/$i.out" &
done
wait

# Count results
echo "200 responses: $(grep -c '200' "$tmpdir"/*.out)"
echo "429 responses: $(grep -c '429' "$tmpdir"/*.out)"
rm -rf "$tmpdir"
```

Expected: Approximately 20 responses with `200`, and 10 with `429 Too Many Requests`.

---

## 18. Full End-to-End Flow Summary

Run this complete sequence to test the entire platform in one pass. Follow each step in order.

```
✅ Step 1:  docker-compose up -d (Section 2)
✅ Step 2:  Import Postman collection (Section 3)
✅ Step 3:  Register testuser1, testuser2, adminuser (Section 4)
✅ Step 4:  Log in as testuser1, set accessToken (Section 4.6)
✅ Step 5:  Browse products, note a productId (Section 7.1)
✅ Step 6:  Create a new product as adminuser (Section 7.5)
✅ Step 7:  Add 2 products to cart as testuser1 (Section 9.7)
✅ Step 8:  Create an order (Section 10.2)
           → Check Mailhog: Welcome + Order Confirmation emails
           → Check RabbitMQ: Events flowing (Section 11)
           → Note the orderNumber
✅ Step 9:  Watch SAGA in RabbitMQ — order goes to Confirmed
           → Check Mailhog: Stock Confirmed email
           → Check Kibana: Trace with Correlation ID (Section 12)
✅ Step 10: Initiate payment (Section 13.1)
✅ Step 11: Verify payment (Section 13.3)
           → Check Mailhog: Payment Receipt email
           → Check Order status → should reflect payment
✅ Step 12: Save a card (Section 13.9)
✅ Step 13: Test SAGA compensation — out-of-stock order (Section 16.1)
           → Check Mailhog: Cancellation email
✅ Step 14: Test circuit breaker — stop/start products service (Section 15)
✅ Step 15: Test negative scenarios (Section 17)
           → IDOR cross-user tests
           → Validation failures
           → Rate limiting
✅ Step 16: Check final state in Kibana — all logs present (Section 12.7)
✅ Step 17: Check all RabbitMQ queues empty — no unprocessed messages (Section 11.7)
```

---

## Appendix A — Portal Reference

| Portal | URL | Credentials |
|--------|-----|-------------|
| Keycloak Admin | http://localhost:8090 | admin / admin |
| RabbitMQ | http://localhost:15672 | guest / guest |
| Mailhog (email) | http://localhost:8025 | No login |
| Kibana | http://localhost:5601 | No login (dev mode) |
| Elasticsearch | http://localhost:9200 | No login |
| Products Swagger | http://localhost:5077/swagger | No login |
| Orders Swagger | http://localhost:5080/swagger | No login |

## Appendix B — Docker Commands Reference

```bash
# Start all services
docker-compose up -d

# Stop all services (keeps data)
docker-compose down

# Stop all services and delete all data (clean slate)
docker-compose down -v

# Restart a single service
docker-compose restart ak-order-api

# View logs of a service (follow mode)
docker logs antkart-order-api --follow

# View last 50 lines of logs
docker logs antkart-order-api --tail 50

# Check all container statuses
docker ps --format "table {{.Names}}\t{{.Status}}"

# Connect to PostgreSQL
docker exec -it antkart-postgres psql -U antkart -d AKOrdersDb
```

## Appendix C — Common Problems and Fixes

| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| `401 Unauthorized` on every request | Token expired (tokens last 5 min) | Log in again via POST Login, copy new token to `accessToken` variable |
| `503 Service Unavailable` from Gateway | Downstream service is down | Run `docker ps` — restart the stopped container |
| `Connection refused` on port 5077 etc. | Service isn't running on that port | Run `docker-compose up -d` — check `docker ps` |
| Mailhog shows no emails | Notification service not started | Check `docker logs antkart-notification-api` |
| RabbitMQ shows no queues | Services haven't connected yet | Wait 30 seconds after `docker-compose up`, then refresh |
| Kibana shows "No results" | Data view not created, or time range too narrow | Create data view (Section 12.2), set time to "Last 1 hour" |
| 409 Conflict on user registration | Username already taken | Use a different username |
| Order stuck in `Pending` | SAGA not processing | Check RabbitMQ for stuck messages; check `docker logs antkart-order-api` |
| Payment verification returns 400 | Wrong signature | Recompute HMAC-SHA256 with exact format: `{razorpayOrderId}|{razorpayPaymentId}` |
