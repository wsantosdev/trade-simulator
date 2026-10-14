# Trading Simulator

## Run the account API and event broker

From the repository root, start the API, PostgreSQL database, and RabbitMQ:

```sh
docker compose up --build
```

The API is available at `http://localhost:8080`; OpenAPI is exposed in the Development environment. PostgreSQL data is kept in the `postgres-data` Docker volume. At startup, the API executes the version-controlled and embedded `Account/TradingSimulator.Account/Infrastructure/Database/schema.sql` after PostgreSQL becomes healthy. RabbitMQ management is available at `http://localhost:15672` using the development-only credentials `account` / `account-local`.

## Run the Angular account UI

```sh
cd UI
npm install
npm start
```

Open `http://localhost:4200/account` (other screens: `/orders`, `/portfolio`, `/quotes`, `/offerbook`; Exchange UI on `http://localhost:4204`). The UI proxies `/api` requests to the Docker API at `http://localhost:8080` and sends an `X-Correlation-Id` on each request. The API also creates a correlation ID if the header is omitted and returns it in the response.

## Account operations

- Open a current account with available and provisioned balances at zero.
- Deposit an amount greater than zero up to $1,000,000 per operation.
- Withdraw up to the available balance, including a withdrawal that reduces it to zero.
- Provision an amount up to the available balance for a purchase order.

Provisioning reduces the available balance and increases the provisioned balance. These requirements do not define a release or settlement flow for provisioned amounts, so none is exposed yet.
