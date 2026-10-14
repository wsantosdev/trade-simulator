-- Um schema por módulo e um usuário por schema, com leitura/escrita apenas no próprio schema.
-- Credenciais exclusivas para o ambiente de desenvolvimento local.

REVOKE ALL ON SCHEMA public FROM PUBLIC;

CREATE ROLE account LOGIN PASSWORD 'account-local';
CREATE SCHEMA account AUTHORIZATION account;
ALTER ROLE account SET search_path = account;

CREATE ROLE orders LOGIN PASSWORD 'orders-local';
CREATE SCHEMA orders AUTHORIZATION orders;
ALTER ROLE orders SET search_path = orders;

CREATE ROLE portfolio LOGIN PASSWORD 'portfolio-local';
CREATE SCHEMA portfolio AUTHORIZATION portfolio;
ALTER ROLE portfolio SET search_path = portfolio;

CREATE ROLE exchange LOGIN PASSWORD 'exchange-local';
CREATE SCHEMA exchange AUTHORIZATION exchange;
ALTER ROLE exchange SET search_path = exchange;
