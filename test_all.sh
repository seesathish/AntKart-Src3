#!/bin/bash
PASS=0
FAIL=0
RESULTS=()

check() {
  local label=$1
  local code=$2
  local expected=$3
  if [ "$code" = "$expected" ]; then
    PASS=$((PASS+1))
    RESULTS+=("  [PASS] $label (HTTP $code)")
  else
    FAIL=$((FAIL+1))
    RESULTS+=("  [FAIL] $label (expected $expected, got $code)")
  fi
}

echo "=== Login ==="
ADMIN_RESP=$(curl -s -X POST http://localhost:8084/api/auth/login -H "Content-Type: application/json" -d '{"username":"admin","password":"admin123"}')
ADMIN_TOKEN=$(echo "$ADMIN_RESP" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)

USER_RESP=$(curl -s -X POST http://localhost:8084/api/auth/login -H "Content-Type: application/json" -d '{"username":"user1","password":"user123"}')
USER_TOKEN=$(echo "$USER_RESP" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)

ADMIN2_RESP=$(curl -s -X POST http://localhost:8084/api/auth/login -H "Content-Type: application/json" -d '{"username":"admin2","password":"Admin2Pass!"}')
ADMIN2_TOKEN=$(echo "$ADMIN2_RESP" | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)

echo "Admin  token: ${#ADMIN_TOKEN} chars"
echo "User1  token: ${#USER_TOKEN} chars"
echo "Admin2 token: ${#ADMIN2_TOKEN} chars"
echo ""

# ============================================================
echo "=== [AK.UserIdentity] ==="
# ============================================================
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8084/health)
check "Health check" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8084/api/auth/login \
  -H "Content-Type: application/json" -d '{"username":"admin","password":"admin123"}')
check "POST /api/auth/login (admin)" $CODE 200

TS=$(date +%s)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8084/api/auth/register \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"testrun$TS\",\"email\":\"testrun$TS@test.com\",\"password\":\"TestRun99!\",\"firstName\":\"Test\",\"lastName\":\"Run\"}")
check "POST /api/auth/register (new user)" $CODE 201

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $ADMIN_TOKEN" http://localhost:8084/api/auth/me)
check "GET /api/auth/me (admin token)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" http://localhost:8084/api/auth/me)
check "GET /api/auth/me (user1 token)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8084/api/auth/me)
check "GET /api/auth/me (no token -> 401)" $CODE 401

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $ADMIN_TOKEN" http://localhost:8084/api/admin/users)
check "GET /api/admin/users (admin)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" http://localhost:8084/api/admin/users)
check "GET /api/admin/users (user -> 403)" $CODE 403

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $ADMIN2_TOKEN" http://localhost:8084/api/admin/users)
check "GET /api/admin/users (admin2 token)" $CODE 200
echo ""

# ============================================================
echo "=== [AK.Products] ==="
# ============================================================
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/health)
check "Health check" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products?page=1&pageSize=5")
check "GET /products?page=1 (anon)" $CODE 200

PRODUCT_JSON=$(curl -s "http://localhost:8080/api/v1/products?page=1&pageSize=1")
PRODUCT_ID=$(echo "$PRODUCT_JSON" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
PRODUCT_SKU=$(echo "$PRODUCT_JSON" | grep -o '"sku":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "  Sample product ID: $PRODUCT_ID  SKU: $PRODUCT_SKU"

PROD_DETAIL=$(curl -s "http://localhost:8080/api/v1/products/$PRODUCT_ID")
CODE=$(echo "$PROD_DETAIL" | grep -o '"id"' | head -1)
if [ -n "$CODE" ]; then
  check "GET /products/{id} (anon)" "200" 200
else
  check "GET /products/{id} (anon)" "500" 200
fi
DISC_PRICE=$(echo "$PROD_DETAIL" | grep -o '"discountPrice":[^,}]*')
echo "  Discount field: $DISC_PRICE"

CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products/categories")
check "GET /products/categories (anon)" $CODE 200

CATS=$(curl -s "http://localhost:8080/api/v1/products/categories")
echo "  Available categories: $CATS"

CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products?category=Men&pageSize=5")
check "GET /products?category=Men (anon)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products?category=Women&pageSize=5")
check "GET /products?category=Women (anon)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products?category=Men&subCategory=Shirts&pageSize=5")
check "GET /products?category=Men&subCategory=Shirts (anon)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products/category/Men?pageSize=5")
check "GET /products/category/Men (anon)" $CODE 200

NEWPROD=$(curl -s -w "\n%{http_code}" -X POST http://localhost:8080/api/v1/products \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d "{\"name\":\"Test Shirt CI\",\"description\":\"Automated test\",\"sku\":\"TST-CI-$TS\",\"brand\":\"TestBrand\",\"categoryName\":\"Sports\",\"subCategoryName\":\"Jerseys\",\"price\":29.99,\"currency\":\"USD\",\"stockQuantity\":10,\"sizes\":[\"M\"],\"colors\":[\"Blue\"]}")
NEW_CODE=$(echo "$NEWPROD" | tail -1)
NEW_ID=$(echo "$NEWPROD" | head -1 | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
check "POST /products (admin, new category Sports)" $NEW_CODE 201
echo "  Created ID: $NEW_ID"

if [ -n "$NEW_ID" ]; then
  CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products?category=Sports")
  check "GET /products?category=Sports (new data-driven category visible)" $CODE 200
fi

CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8080/api/v1/products \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"Anon\",\"description\":\"x\",\"sku\":\"TST-AN-$TS\",\"brand\":\"X\",\"categoryName\":\"Men\",\"price\":9.99,\"currency\":\"USD\",\"stockQuantity\":1,\"sizes\":[],\"colors\":[]}")
check "POST /products (no auth -> 401)" $CODE 401

CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8080/api/v1/products \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d "{\"name\":\"User\",\"description\":\"x\",\"sku\":\"TST-US-$TS\",\"brand\":\"X\",\"categoryName\":\"Men\",\"price\":9.99,\"currency\":\"USD\",\"stockQuantity\":1,\"sizes\":[],\"colors\":[]}")
check "POST /products (user role -> 403)" $CODE 403

if [ -n "$NEW_ID" ]; then
  CODE=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "http://localhost:8080/api/v1/products/$NEW_ID" \
    -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
    -d '{"name":"Test Shirt CI Updated","description":"Updated","brand":"TestBrand","price":34.99,"stockQuantity":5}')
  check "PUT /products/{id} (admin)" $CODE 200

  CODE=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "http://localhost:8080/api/v1/products/$NEW_ID" \
    -H "Authorization: Bearer $ADMIN_TOKEN")
  check "DELETE /products/{id} (admin)" $CODE 204
fi
echo ""

# ============================================================
echo "=== [AK.Discount gRPC] ==="
# ============================================================
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8081/health)
check "Health check" $CODE 200

DISC_CHECK=$(curl -s "http://localhost:8080/api/v1/products/$PRODUCT_ID")
if echo "$DISC_CHECK" | grep -q '"discountPrice"'; then
  check "Products show discountPrice field (gRPC integration)" "200" 200
else
  check "Products show discountPrice field (gRPC integration)" "MISSING" 200
fi
echo ""

# ============================================================
echo "=== [AK.ShoppingCart] ==="
# Routes: POST /items, GET /, PUT /items/{productId}
#         DELETE /items/{productId}, DELETE /
# ============================================================
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8082/health)
check "Health check" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8082/api/v1/cart)
check "GET /cart (no auth -> 401)" $CODE 401

# Add item first (POST /{userId}/items)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "http://localhost:8082/api/v1/cart/items" \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d "{\"productId\":\"$PRODUCT_ID\",\"productName\":\"Test Shirt\",\"sku\":\"MEN-SHIR-001\",\"price\":29.99,\"quantity\":2}")
check "POST /cart/items (user auth)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" http://localhost:8082/api/v1/cart)
check "GET /cart (user auth, has items)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "http://localhost:8082/api/v1/cart/items" \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{"productId":"prod-admin-1","productName":"Admin Item","sku":"ADM-ITEM-001","price":99.99,"quantity":1}')
check "POST /cart/items (admin adds to own cart)" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $ADMIN_TOKEN" http://localhost:8082/api/v1/cart)
check "GET /cart (admin reads own cart)" $CODE 200

# Update quantity
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "http://localhost:8082/api/v1/cart/items/$PRODUCT_ID" \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d '{"quantity":5}')
check "PUT /cart/items/{productId} (update qty)" $CODE 200

# Remove single item
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "http://localhost:8082/api/v1/cart/items/$PRODUCT_ID" \
  -H "Authorization: Bearer $USER_TOKEN")
check "DELETE /cart/items/{productId} (remove item)" $CODE 200

# Clear cart
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "http://localhost:8082/api/v1/cart" \
  -H "Authorization: Bearer $USER_TOKEN")
check "DELETE /cart (clear, user auth)" $CODE 204

CODE=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "http://localhost:8082/api/v1/cart" \
  -H "Authorization: Bearer $ADMIN_TOKEN")
check "DELETE /cart (clear, admin)" $CODE 204
echo ""

# ============================================================
echo "=== [AK.Order] ==="
# Routes: GET /api/orders, GET /api/orders/{guid}, GET /api/orders/me
#         POST /api/orders, PUT /api/orders/{guid}/status, DELETE /api/orders/{guid}
# ============================================================
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8083/health)
check "Health check" $CODE 200

CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8083/api/orders)
check "GET /api/orders (no auth -> 401)" $CODE 401

CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" http://localhost:8083/api/orders)
check "GET /api/orders (user auth)" $CODE 200

NEW_ORDER=$(curl -s -w "\n%{http_code}" -X POST http://localhost:8083/api/orders \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d "{\"shippingAddress\":{\"fullName\":\"John Doe\",\"addressLine1\":\"123 Main St\",\"city\":\"New York\",\"state\":\"NY\",\"postalCode\":\"10001\",\"country\":\"USA\",\"phone\":\"555-1234\"},\"items\":[{\"productId\":\"$PRODUCT_ID\",\"productName\":\"Test Product\",\"sku\":\"MEN-SHIR-001\",\"price\":29.99,\"quantity\":1}]}")
ORDER_CODE=$(echo "$NEW_ORDER" | tail -1)
ORDER_ID=$(echo "$NEW_ORDER" | head -1 | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
check "POST /api/orders (create, user auth)" $ORDER_CODE 201
echo "  Created order ID: $ORDER_ID"

if [ -n "$ORDER_ID" ]; then
  CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" "http://localhost:8083/api/orders/$ORDER_ID")
  check "GET /api/orders/{id} (user auth)" $CODE 200

  CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" "http://localhost:8083/api/orders/me")
  check "GET /api/orders/me (user auth)" $CODE 200

  # The SAGA moves the order Pending→Confirmed via 3 RabbitMQ hops (OrderCreated→StockReserved→Confirmed).
  # Poll until Confirmed (status=2) or timeout after 15s to avoid flaky timing.
  SAGA_WAIT=0
  while [ $SAGA_WAIT -lt 15 ]; do
    ORDER_STATUS=$(curl -s -H "Authorization: Bearer $ADMIN_TOKEN" "http://localhost:8083/api/orders/$ORDER_ID" | grep -o '"status":"[^"]*"' | cut -d'"' -f4)
    [ "$ORDER_STATUS" = "Confirmed" ] && break
    sleep 1; SAGA_WAIT=$((SAGA_WAIT + 1))
  done
  CODE=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "http://localhost:8083/api/orders/$ORDER_ID/status" \
    -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
    -d '{"newStatus":3}')
  check "PUT /api/orders/{id}/status admin -> Processing (admin)" $CODE 200

  CODE=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "http://localhost:8083/api/orders/$ORDER_ID" \
    -H "Authorization: Bearer $USER_TOKEN")
  check "DELETE /api/orders/{id} cancel (user)" $CODE 204
fi
echo ""

# ============================================================
echo "=== [AK.Payments] ==="
# Routes: POST /api/payments/initiate, POST /api/payments/verify
#         GET /api/payments/{id}, GET /api/payments/order/{orderId}
#         GET /api/payments/user/{userId}
#         GET /api/payments/cards/user/{userId}, POST /api/payments/cards/save
#         DELETE /api/payments/cards/{id}
# ============================================================
CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8085/health)
check "Health check" $CODE 200

# Auth guard — no token
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8085/api/payments/initiate \
  -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","amount":29.99,"method":1}')
check "POST /api/payments/initiate (no auth -> 401)" $CODE 401

# Validation — missing amount (user auth)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8085/api/payments/initiate \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","method":1}')
check "POST /api/payments/initiate (missing amount -> 400)" $CODE 400

# Validation — zero amount
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8085/api/payments/initiate \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d '{"orderId":"00000000-0000-0000-0000-000000000001","amount":0,"method":1}')
check "POST /api/payments/initiate (zero amount -> 400)" $CODE 400

# Create a fresh order for payment testing
PAY_ORDER=$(curl -s -w "\n%{http_code}" -X POST http://localhost:8083/api/orders \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d "{\"shippingAddress\":{\"fullName\":\"Pay Test\",\"addressLine1\":\"1 Pay St\",\"city\":\"Mumbai\",\"state\":\"MH\",\"postalCode\":\"400001\",\"country\":\"IN\",\"phone\":\"+91-9999999999\"},\"items\":[{\"productId\":\"$PRODUCT_ID\",\"productName\":\"Test Product\",\"sku\":\"MEN-SHIR-001\",\"price\":29.99,\"quantity\":1}]}")
PAY_ORDER_CODE=$(echo "$PAY_ORDER" | tail -1)
PAY_ORDER_BODY=$(echo "$PAY_ORDER" | head -1)
PAY_ORDER_ID=$(echo "$PAY_ORDER_BODY" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
PAY_ORDER_NUMBER=$(echo "$PAY_ORDER_BODY" | grep -o '"orderNumber":"[^"]*"' | cut -d'"' -f4)
echo "  Payment test order: $PAY_ORDER_ID  $PAY_ORDER_NUMBER (HTTP $PAY_ORDER_CODE)"

# Initiate payment (hits Razorpay sandbox — requires internet from Docker)
if [ -n "$PAY_ORDER_ID" ]; then
  PAY_RESP=$(curl -s -w "\n%{http_code}" -X POST http://localhost:8085/api/payments/initiate \
    -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
    -d "{\"orderId\":\"$PAY_ORDER_ID\",\"orderNumber\":\"$PAY_ORDER_NUMBER\",\"amount\":29.99,\"method\":1}")
  PAY_CODE=$(echo "$PAY_RESP" | tail -1)
  PAY_BODY=$(echo "$PAY_RESP" | head -1)
  PAY_ID=$(echo "$PAY_BODY" | grep -o '"paymentId":"[^"]*"' | cut -d'"' -f4)
  RZP_ORDER_ID=$(echo "$PAY_BODY" | grep -o '"razorpayOrderId":"[^"]*"' | cut -d'"' -f4)
  check "POST /api/payments/initiate (user auth, valid order)" $PAY_CODE 200
  echo "  PaymentId: $PAY_ID  RazorpayOrderId: $RZP_ORDER_ID"
fi

# Get payment by order id
if [ -n "$PAY_ORDER_ID" ]; then
  CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" \
    "http://localhost:8085/api/payments/order/$PAY_ORDER_ID")
  check "GET /api/payments/order/{orderId} (user auth)" $CODE 200
fi

# Get payment by id
if [ -n "$PAY_ID" ]; then
  CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" \
    "http://localhost:8085/api/payments/$PAY_ID")
  check "GET /api/payments/{id} (user auth)" $CODE 200
fi

# Get user payments — positive
CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" \
  "http://localhost:8085/api/payments/me")
check "GET /api/payments/me (user auth)" $CODE 200

# Get user payments — no auth (negative)
CODE=$(curl -s -o /dev/null -w "%{http_code}" \
  "http://localhost:8085/api/payments/me")
check "GET /api/payments/me (no auth -> 401)" $CODE 401

# Verify with invalid signature (negative)
if [ -n "$PAY_ORDER_ID" ] && [ -n "$RZP_ORDER_ID" ]; then
  CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8085/api/payments/verify \
    -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
    -d "{\"orderId\":\"$PAY_ORDER_ID\",\"razorpayOrderId\":\"$RZP_ORDER_ID\",\"razorpayPaymentId\":\"pay_invalid_test\",\"razorpaySignature\":\"invalidsignature123\"}")
  check "POST /api/payments/verify (invalid signature -> 400)" $CODE 400
fi

# Saved cards — no auth (negative)
CODE=$(curl -s -o /dev/null -w "%{http_code}" \
  "http://localhost:8085/api/payments/cards")
check "GET /api/payments/cards (no auth -> 401)" $CODE 401

# Saved cards — user auth (positive, returns empty list)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" \
  "http://localhost:8085/api/payments/cards")
check "GET /api/payments/cards (user auth)" $CODE 200

# Save card — missing tokenId (negative)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8085/api/payments/cards/save \
  -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
  -d '{"razorpayCustomerId":"cust_test","cardNetwork":"Visa","last4":"1111","cardType":"credit","cardName":"Test Card"}')
check "POST /api/payments/cards/save (missing razorpayPaymentId -> 400)" $CODE 400

# Save card — no auth (negative)
CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8085/api/payments/cards/save \
  -H "Content-Type: application/json" \
  -d '{"razorpayCustomerId":"cust_test","razorpayPaymentId":"pay_test","customerName":"Test User","customerEmail":"test@test.com","customerContact":"9999999999"}')
check "POST /api/payments/cards/save (no auth -> 401)" $CODE 401

# Cleanup: cancel the payment test order
if [ -n "$PAY_ORDER_ID" ]; then
  curl -s -o /dev/null -X DELETE "http://localhost:8083/api/orders/$PAY_ORDER_ID" \
    -H "Authorization: Bearer $USER_TOKEN"
fi
echo ""

# ============================================================
echo "=== [Concurrent Load Tests] ==="
# ============================================================
TMPDIR=$(mktemp -d)

echo "  Launching 20x parallel Products GET..."
for i in $(seq 1 20); do
  (CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:8080/api/v1/products?page=1&pageSize=10")
   echo $CODE > "$TMPDIR/p_$i") &
done
wait
C_PASS=0; C_FAIL=0
for i in $(seq 1 20); do
  C=$(cat "$TMPDIR/p_$i" 2>/dev/null)
  if [ "$C" = "200" ]; then C_PASS=$((C_PASS+1)); else C_FAIL=$((C_FAIL+1)); fi
done
echo "  Products 20x: $C_PASS pass / $C_FAIL fail"
check "20x concurrent Products GET" "$C_FAIL" "0"

echo "  Launching 10x parallel Cart add (user1)..."
for i in $(seq 1 10); do
  (CODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST "http://localhost:8082/api/v1/cart/items" \
    -H "Authorization: Bearer $USER_TOKEN" -H "Content-Type: application/json" \
    -d "{\"productId\":\"prod-conc-$i\",\"productName\":\"Item $i\",\"sku\":\"CON-ITEM-00$i\",\"price\":9.99,\"quantity\":1}")
   echo $CODE > "$TMPDIR/ca_$i") &
done
wait
CA_PASS=0; CA_FAIL=0
for i in $(seq 1 10); do
  C=$(cat "$TMPDIR/ca_$i" 2>/dev/null)
  if [ "$C" = "200" ]; then CA_PASS=$((CA_PASS+1)); else CA_FAIL=$((CA_FAIL+1)); fi
done
echo "  Cart 10x concurrent: $CA_PASS pass / $CA_FAIL fail"
check "10x concurrent Cart add" "$CA_FAIL" "0"

echo "  Launching 10x parallel Order GET (user1)..."
for i in $(seq 1 10); do
  (CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $USER_TOKEN" http://localhost:8083/api/orders)
   echo $CODE > "$TMPDIR/o_$i") &
done
wait
O_PASS=0; O_FAIL=0
for i in $(seq 1 10); do
  C=$(cat "$TMPDIR/o_$i" 2>/dev/null)
  if [ "$C" = "200" ]; then O_PASS=$((O_PASS+1)); else O_FAIL=$((O_FAIL+1)); fi
done
echo "  Order 10x concurrent: $O_PASS pass / $O_FAIL fail"
check "10x concurrent Order GET" "$O_FAIL" "0"

echo "  Launching 5x parallel admin Products pages..."
for i in $(seq 1 5); do
  (CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $ADMIN_TOKEN" "http://localhost:8080/api/v1/products?page=$i&pageSize=10")
   echo $CODE > "$TMPDIR/adm_$i") &
done
wait
ADM_PASS=0; ADM_FAIL=0
for i in $(seq 1 5); do
  C=$(cat "$TMPDIR/adm_$i" 2>/dev/null)
  if [ "$C" = "200" ]; then ADM_PASS=$((ADM_PASS+1)); else ADM_FAIL=$((ADM_FAIL+1)); fi
done
echo "  Admin paged 5x: $ADM_PASS pass / $ADM_FAIL fail"
check "5x concurrent Admin Products GET" "$ADM_FAIL" "0"

echo "  Launching 5x parallel UserIdentity /me..."
for i in $(seq 1 5); do
  TOKEN_TMP=$USER_TOKEN
  if [ $((i % 2)) = 0 ]; then TOKEN_TMP=$ADMIN_TOKEN; fi
  (CODE=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $TOKEN_TMP" http://localhost:8084/api/auth/me)
   echo $CODE > "$TMPDIR/ui_$i") &
done
wait
UI_PASS=0; UI_FAIL=0
for i in $(seq 1 5); do
  C=$(cat "$TMPDIR/ui_$i" 2>/dev/null)
  if [ "$C" = "200" ]; then UI_PASS=$((UI_PASS+1)); else UI_FAIL=$((UI_FAIL+1)); fi
done
echo "  UserIdentity /me 5x: $UI_PASS pass / $UI_FAIL fail"
check "5x concurrent UserIdentity /me" "$UI_FAIL" "0"

rm -rf "$TMPDIR"
# Cleanup concurrent test cart
curl -s -o /dev/null -X DELETE "http://localhost:8082/api/v1/cart" -H "Authorization: Bearer $USER_TOKEN"
echo ""

echo "=============================="
echo "  FINAL RESULTS"
echo "=============================="
for r in "${RESULTS[@]}"; do echo "$r"; done
echo ""
TOTAL=$((PASS+FAIL))
echo "  Total: $TOTAL | PASS: $PASS | FAIL: $FAIL"
if [ "$FAIL" = "0" ]; then
  echo "  ALL TESTS PASSED"
else
  echo "  $FAIL TEST(S) FAILED"
fi
