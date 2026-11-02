-- Um schema por microsserviço e um único usuário com leitura/escrita em todos eles.
-- Credenciais exclusivas para o ambiente de desenvolvimento local.

REVOKE ALL ON SCHEMA public FROM PUBLIC;

CREATE ROLE tradesimulator LOGIN PASSWORD 'tradesimulator-local';

CREATE SCHEMA account AUTHORIZATION tradesimulator;
CREATE SCHEMA orders AUTHORIZATION tradesimulator;
CREATE SCHEMA portfolio AUTHORIZATION tradesimulator;
CREATE SCHEMA exchange AUTHORIZATION tradesimulator;
