rem
rem Copyright (c) 2023 Oracle
rem
rem Permission is hereby granted, free of charge, to any person obtaining a
rem copy of this software and associated documentation files (the "Software"),
rem to deal in the Software without restriction, including without limitation
rem the rights to use, copy, modify, merge, publish, distribute, sublicense,
rem and/or sell copies of the Software, and to permit persons to whom the
rem Software is furnished to do so, subject to the following conditions:
rem
rem The above copyright notice and this permission notice shall be included in
rem all copies or substantial portions rem of the Software.
rem
rem THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
rem IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
rem FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
rem THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
rem LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
rem FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
rem DEALINGS IN THE SOFTWARE.
rem
rem NAME
rem   co_create.sql - Creates schema objects for CO (Customer Orders) schema
rem
rem DESCRIPTION
rem   This script creates tables, associated constraints,
rem      indexes, and comments in the CO schema.
rem
rem SCHEMA VERSION
rem   21
rem
rem RELEASE DATE
rem   08-FEB-2022
rem
rem SUPPORTED with DB VERSIONS
rem   19c and higher
rem
rem MAJOR CHANGES IN THIS RELEASE
rem
rem
rem SCHEMA DEPENDENCIES AND REQUIREMENTS
rem   This script is called from the co_install.sql script
rem
rem INSTALL INSTRUCTIONS
rem    Run the co_install.sql script to call this script
rem
rem --------------------------------------------------------------------------

rem ********************************************************************
rem Create the CUSTOMERS table to hold customer information

Prompt ******  Creating CUSTOMERS table ....

CREATE TABLE customers
(
  customer_id     INTEGER GENERATED BY DEFAULT ON NULL AS IDENTITY,
  email_address   VARCHAR2(255 CHAR) NOT NULL,
  full_name       VARCHAR2(255 CHAR) NOT NULL
);


rem ********************************************************************
rem Create the STORES table to hold store information

Prompt ******  Creating STORES table ....

CREATE TABLE stores
(
  store_id            INTEGER GENERATED BY DEFAULT ON NULL AS IDENTITY,
  store_name          VARCHAR2(255 CHAR) NOT NULL,
  web_address         VARCHAR2(100 CHAR),
  physical_address    VARCHAR2(512 CHAR),
  latitude            NUMBER(9,6),
  longitude           NUMBER(9,6),
  logo                BLOB,
  logo_mime_type      VARCHAR2(512 CHAR),
  logo_filename       VARCHAR2(512 CHAR),
  logo_charset        VARCHAR2(512 CHAR),
  logo_last_updated   DATE
);


rem ********************************************************************
rem Create the PRODUCTS table to hold product information

Prompt ******  Creating PRODUCTS table ....

CREATE TABLE products
(
  product_id           INTEGER GENERATED BY DEFAULT ON NULL AS IDENTITY,
  product_name         VARCHAR2(255 CHAR) NOT NULL,
  unit_price           NUMBER(10,2),
  product_details      BLOB,
  product_image        BLOB,
  image_mime_type      VARCHAR2(512 CHAR),
  image_filename       VARCHAR2(512 CHAR),
  image_charset        VARCHAR2(512 CHAR),
  image_last_updated   DATE
);

rem ********************************************************************
rem Create the ORDERS table to hold orders information

Prompt ******  Creating ORDERS table ....

CREATE TABLE orders
(
  order_id       INTEGER GENERATED BY DEFAULT ON NULL AS IDENTITY,
  order_tms      TIMESTAMP NOT NULL,
  customer_id    INTEGER NOT NULL,
  order_status   VARCHAR2(10 CHAR) NOT NULL,
  store_id       INTEGER NOT NULL
);

rem ********************************************************************
rem Create the SHIPMENTS table to hold shipment information

Prompt ******  Creating SHIPMENTS table ....

CREATE TABLE shipments
(
  shipment_id        INTEGER GENERATED BY DEFAULT ON NULL AS IDENTITY,
  store_id           INTEGER NOT NULL,
  customer_id        INTEGER NOT NULL,
  delivery_address   VARCHAR2(512 CHAR) NOT NULL,
  shipment_status    VARCHAR2(100 CHAR) NOT NULL
);

rem ********************************************************************
rem Create the ORDER_ITEMS table to hold order item information for orders

Prompt ******  Creating ORDER_ITEMS table ....

CREATE TABLE order_items
(
  order_id       INTEGER NOT NULL,
  line_item_id   INTEGER NOT NULL,
  product_id     INTEGER NOT NULL,
  unit_price     NUMBER(10,2) NOT NULL,
  quantity       INTEGER NOT NULL,
  shipment_id    INTEGER
);

rem ********************************************************************
rem Create the INVENTORY table to hold inventory information

Prompt ******  Creating INVENTORY table ....

CREATE TABLE inventory
(
  inventory_id        INTEGER GENERATED BY DEFAULT ON NULL AS IDENTITY,
  store_id            INTEGER NOT NULL,
  product_id          INTEGER NOT NULL,
  product_inventory   INTEGER NOT NULL
);

rem ********************************************************************
rem Create views

Prompt ******  Create views

rem ********************************************************************
rem A view for a summary of who placed each order and what they bought

CREATE OR REPLACE VIEW customer_order_products AS
  SELECT o.order_id, o.order_tms, o.order_status,
         c.customer_id, c.email_address, c.full_name,
         SUM ( oi.quantity * oi.unit_price ) order_total,
         LISTAGG (
           p.product_name, ', '
           ON OVERFLOW TRUNCATE '...' WITH COUNT
         ) WITHIN GROUP ( ORDER BY oi.line_item_id ) items
  FROM   orders o
  JOIN   order_items oi
  ON     o.order_id = oi.order_id
  JOIN   customers c
  ON     o.customer_id = c.customer_id
  JOIN   products p
  ON     oi.product_id = p.product_id
  GROUP  BY o.order_id, o.order_tms, o.order_status,
         c.customer_id, c.email_address, c.full_name;

rem ********************************************************************
rem A view for a summary of what was purchased at each location,
rem    including summaries each store, order status and overall total

CREATE OR REPLACE VIEW store_orders AS
  SELECT CASE
           grouping_id ( store_name, order_status )
           WHEN 1 THEN 'STORE TOTAL'
           WHEN 2 THEN 'STATUS TOTAL'
           WHEN 3 THEN 'GRAND TOTAL'
         END total,
         s.store_name,
         COALESCE ( s.web_address, s.physical_address ) address,
         s.latitude, s.longitude,
         o.order_status,
         COUNT ( DISTINCT o.order_id ) order_count,
         SUM ( oi.quantity * oi.unit_price ) total_sales
  FROM   stores s
  JOIN   orders o
  ON     s.store_id = o.store_id
  JOIN   order_items oi
  ON     o.order_id = oi.order_id
  GROUP  BY GROUPING SETS (
    ( s.store_name, COALESCE ( s.web_address, s.physical_address ), s.latitude, s.longitude ),
    ( s.store_name, COALESCE ( s.web_address, s.physical_address ), s.latitude, s.longitude, o.order_status ),
    o.order_status,
    ()
  );

rem ********************************************************************
rem A relational view of the reviews stored in the JSON for each product

CREATE OR REPLACE VIEW product_reviews AS
  SELECT p.product_name, r.rating,
         ROUND (
           AVG ( r.rating ) over (
             PARTITION BY product_name
           ),
           2
         ) avg_rating,
         r.review
  FROM   products p,
         JSON_TABLE (
           p.product_details, '$'
           COLUMNS (
             NESTED PATH '$.reviews[*]'
             COLUMNS (
               rating INTEGER PATH '$.rating',
               review VARCHAR2(4000) PATH '$.review'
             )
           )
         ) r;

rem ********************************************************************
rem A view for a summary of the total sales per product and order status

CREATE OR REPLACE VIEW product_orders AS
  SELECT p.product_name, o.order_status,
         SUM ( oi.quantity * oi.unit_price ) total_sales,
         COUNT (*) order_count
  FROM   orders o
  JOIN   order_items oi
  ON     o.order_id = oi.order_id
  JOIN   customers c
  ON     o.customer_id = c.customer_id
  JOIN   products p
  ON     oi.product_id = p.product_id
  GROUP  BY p.product_name, o.order_status;


rem ********************************************************************
rem Create indexes

Prompt ******  Creating indexes ...

CREATE INDEX customers_name_i          ON customers   ( full_name );
CREATE INDEX orders_customer_id_i      ON orders      ( customer_id );
CREATE INDEX orders_store_id_i         ON orders      ( store_id );
CREATE INDEX shipments_store_id_i      ON shipments   ( store_id );
CREATE INDEX shipments_customer_id_i   ON shipments   ( customer_id );
CREATE INDEX order_items_shipment_id_i ON order_items ( shipment_id );
CREATE INDEX inventory_product_id_i    ON inventory   ( product_id );

rem ********************************************************************
rem Create constraints

Prompt ******  Adding constraints to tables ...

ALTER TABLE customers ADD CONSTRAINT customers_pk PRIMARY KEY (customer_id);

ALTER TABLE customers ADD CONSTRAINT customers_email_u UNIQUE (email_address);

ALTER TABLE stores ADD CONSTRAINT stores_pk PRIMARY KEY (store_id);

ALTER TABLE stores ADD CONSTRAINT store_name_u UNIQUE (store_name);

ALTER TABLE stores ADD CONSTRAINT store_at_least_one_address_c
  CHECK (
    web_address IS NOT NULL or physical_address IS NOT NULL
  );

ALTER TABLE products ADD CONSTRAINT products_pk PRIMARY KEY (product_id);

ALTER TABLE products ADD CONSTRAINT products_json_c
                     CHECK ( product_details is json );

ALTER TABLE orders ADD CONSTRAINT orders_pk PRIMARY KEY (order_id);

ALTER TABLE orders ADD CONSTRAINT orders_customer_id_fk
   FOREIGN KEY (customer_id) REFERENCES customers (customer_id);

ALTER TABLE orders ADD CONSTRAINT  orders_status_c
                  CHECK ( order_status in
                    ( 'CANCELLED','COMPLETE','OPEN','PAID','REFUNDED','SHIPPED'));

ALTER TABLE orders ADD CONSTRAINT orders_store_id_fk FOREIGN KEY (store_id)
   REFERENCES stores (store_id);

ALTER TABLE shipments ADD CONSTRAINT shipments_pk PRIMARY KEY (shipment_id);

ALTER TABLE shipments ADD CONSTRAINT shipments_store_id_fk
   FOREIGN KEY (store_id) REFERENCES stores (store_id);

ALTER TABLE shipments ADD CONSTRAINT shipments_customer_id_fk
   FOREIGN KEY (customer_id) REFERENCES customers (customer_id);

ALTER TABLE shipments ADD CONSTRAINT shipment_status_c
                  CHECK ( shipment_status in
                    ( 'CREATED', 'SHIPPED', 'IN-TRANSIT', 'DELIVERED'));

ALTER TABLE order_items ADD CONSTRAINT order_items_pk PRIMARY KEY ( order_id, line_item_id );

ALTER TABLE order_items ADD CONSTRAINT order_items_order_id_fk
   FOREIGN KEY (order_id) REFERENCES orders (order_id);

ALTER TABLE order_items ADD CONSTRAINT order_items_shipment_id_fk
   FOREIGN KEY (shipment_id) REFERENCES shipments (shipment_id);

ALTER TABLE order_items ADD CONSTRAINT order_items_product_id_fk
   FOREIGN KEY (product_id) REFERENCES products (product_id);

ALTER TABLE order_items ADD CONSTRAINT order_items_product_u UNIQUE ( product_id, order_id );

ALTER TABLE inventory ADD CONSTRAINT inventory_pk PRIMARY KEY (inventory_id);

ALTER TABLE inventory ADD CONSTRAINT inventory_store_product_u UNIQUE (store_id, product_id);

ALTER TABLE inventory ADD CONSTRAINT inventory_store_id_fk
   FOREIGN KEY (store_id) REFERENCES stores (store_id);

ALTER TABLE inventory ADD CONSTRAINT inventory_product_id_fk
   FOREIGN KEY (product_id) REFERENCES products (product_id);

rem ********************************************************************
rem Add table column comments

Prompt ******  Adding table column comments ...

COMMENT ON TABLE customers
  IS 'Details of the people placing orders';

COMMENT ON COLUMN customers.customer_id
  IS 'Auto-incrementing primary key';

COMMENT ON COLUMN customers.email_address
  IS 'The email address the person uses to access the account';

COMMENT ON COLUMN customers.full_name
  IS 'What this customer is called';

COMMENT ON TABLE stores
  IS 'Physical and virtual locations where people can purchase products';

COMMENT ON COLUMN stores.store_id
  IS 'Auto-incrementing primary key';

COMMENT ON COLUMN stores.store_name
  IS 'What the store is called';

COMMENT ON COLUMN stores.web_address
  IS 'The URL of a virtual store';

COMMENT ON COLUMN stores.physical_address
  IS 'The postal address of this location';

COMMENT ON COLUMN stores.latitude
  IS 'The north-south position of a physical store';

COMMENT ON COLUMN stores.longitude
  IS 'The east-west position of a physical store';

COMMENT ON COLUMN stores.logo
  IS 'An image used by this store';

COMMENT ON COLUMN stores.logo_mime_type
  IS 'The mime-type of the store logo';

COMMENT ON COLUMN stores.logo_last_updated
  IS 'The date the image was last changed';

COMMENT ON COLUMN stores.logo_filename
  IS 'The name of the file loaded in the image column';

COMMENT ON COLUMN stores.logo_charset
  IS 'The character set used to encode the image';

COMMENT ON TABLE products
  IS 'Details of goods that customers can purchase';

COMMENT ON COLUMN products.product_id
  IS 'Auto-incrementing primary key';

COMMENT ON COLUMN products.unit_price
  IS 'The monetary value of one item of this product';

COMMENT ON COLUMN products.product_details
  IS 'Further details of the product stored in JSON format';

COMMENT ON COLUMN products.product_image
  IS 'A picture of the product';

COMMENT ON COLUMN products.image_mime_type
  IS 'The mime-type of the product image';

COMMENT ON COLUMN products.image_last_updated
  IS 'The date the image was last changed';

COMMENT ON COLUMN products.image_filename
  IS 'The name of the file loaded in the image column';

COMMENT ON COLUMN products.image_charset
  IS 'The character set used to encode the image';

COMMENT ON COLUMN products.product_name
  IS 'What a product is called';

COMMENT ON TABLE orders
  IS 'Details of who made purchases where';

COMMENT ON COLUMN orders.order_id
  IS 'Auto-incrementing primary key';

COMMENT ON COLUMN orders.order_tms
  IS 'When the order was placed';

COMMENT ON COLUMN orders.customer_id
  IS 'Who placed this order';

COMMENT ON COLUMN orders.store_id
  IS 'Where this order was placed';

COMMENT ON COLUMN orders.order_status
  IS 'What state the order is in. Valid values are:
OPEN - the order is in progress
PAID - money has been received from the customer for this order
SHIPPED - the products have been dispatched to the customer
COMPLETE - the customer has received the order
CANCELLED - the customer has stopped the order
REFUNDED - there has been an issue with the order and the money has been returned to the customer';

COMMENT ON TABLE order_items
  IS 'Details of which products the customer has purchased in an order';

COMMENT ON COLUMN order_items.order_id
  IS 'The order these products belong to';

COMMENT ON COLUMN order_items.line_item_id
  IS 'An incrementing number, starting at one for each order';

COMMENT ON COLUMN order_items.product_id
  IS 'Which item was purchased';

COMMENT ON COLUMN order_items.unit_price
  IS 'How much the customer paid for one item of the product';

COMMENT ON COLUMN order_items.quantity
  IS 'How many items of this product the customer purchased';

COMMENT ON COLUMN order_items.shipment_id
  IS 'Where this product will be delivered';

COMMENT ON TABLE customer_order_products
  IS 'A summary of who placed each order and what they bought';

COMMENT ON COLUMN customer_order_products.order_id
  IS 'The primary key of the order';

COMMENT ON COLUMN customer_order_products.order_tms
  IS 'The date and time the order was placed';

COMMENT ON COLUMN customer_order_products.order_status
  IS 'The current state of this order';

COMMENT ON COLUMN customer_order_products.customer_id
  IS 'The primary key of the customer';

COMMENT ON COLUMN customer_order_products.email_address
  IS 'The email address the person uses to access the account';

COMMENT ON COLUMN customer_order_products.full_name
  IS 'What this customer is called';

COMMENT ON COLUMN customer_order_products.order_total
  IS 'The total amount the customer paid for the order';

COMMENT ON COLUMN customer_order_products.items
  IS 'A comma-separated list naming the products in this order';

COMMENT ON TABLE store_orders
  IS 'A summary of what was purchased at each location, including summaries each store, order status and overall total';

COMMENT ON COLUMN store_orders.order_status
  IS 'The current state of this order';

COMMENT ON COLUMN store_orders.total
  IS 'Indicates what type of total is displayed, including Store, Status, or Grand Totals';

COMMENT ON COLUMN store_orders.store_name
  IS 'What the store is called';

COMMENT ON COLUMN store_orders.latitude
  IS 'The north-south position of a physical store';

COMMENT ON COLUMN store_orders.longitude
  IS 'The east-west position of a physical store';

COMMENT ON COLUMN store_orders.address
  IS 'The physical or virtual location of this store';

COMMENT ON COLUMN store_orders.total_sales
  IS 'The total value of orders placed';

COMMENT ON COLUMN store_orders.order_count
  IS 'The total number of orders placed';

COMMENT ON TABLE product_reviews
  IS 'A relational view of the reviews stored in the JSON for each product';

COMMENT ON COLUMN product_reviews.product_name
  IS 'What this product is called';

COMMENT ON COLUMN product_reviews.rating
  IS 'The review score the customer has placed. Range is 1-10';

COMMENT ON COLUMN product_reviews.avg_rating
  IS 'The mean of the review scores for this product';

COMMENT ON COLUMN product_reviews.review
  IS 'The text of the review';

COMMENT ON TABLE product_orders
  IS 'A summary of the state of the orders placed for each product';

COMMENT ON COLUMN product_orders.product_name
  IS 'What this product is called';

COMMENT ON COLUMN product_orders.order_status
  IS 'The current state of these order';

COMMENT ON COLUMN product_orders.total_sales
  IS 'The total value of orders placed';

COMMENT ON COLUMN product_orders.order_count
  IS 'The total number of orders placed';

COMMENT ON TABLE shipments
  IS 'Details of where ordered goods will be delivered';

COMMENT ON COLUMN shipments.shipment_id
  IS 'Auto-incrementing primary key';

COMMENT ON COLUMN shipments.store_id
  IS 'Which location the goods will be transported from';

COMMENT ON COLUMN shipments.customer_id
  IS 'Who this shipment is for';

COMMENT ON COLUMN shipments.delivery_address
  IS 'Where the goods will be transported to';

COMMENT ON COLUMN shipments.shipment_status
  IS 'The current status of the shipment. Valid values are:
CREATED - the shipment is ready for order assignment
SHIPPED - the goods have been dispatched
IN-TRANSIT - the goods are en-route to their destination
DELIVERED - the good have arrived at their destination';

COMMENT ON TABLE inventory
  IS 'Details of the quantity of stock available for products at each location';

COMMENT ON COLUMN inventory.inventory_id
  IS 'Auto-incrementing primary key';

COMMENT ON COLUMN inventory.store_id
  IS 'Which location the goods are located at';

COMMENT ON COLUMN inventory.product_id
  IS 'Which item this stock is for';

COMMENT ON COLUMN inventory.product_inventory
  IS 'The current quantity in stock';
