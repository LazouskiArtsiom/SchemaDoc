CREATE DATABASE librarydb;
\c librarydb;

CREATE TABLE authors (
    id SERIAL PRIMARY KEY,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    birth_date DATE,
    nationality VARCHAR(50),
    bio TEXT
);

CREATE TABLE publishers (
    id SERIAL PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    country VARCHAR(100),
    founded_year INT
);

CREATE TABLE categories (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    description TEXT
);

CREATE TABLE books (
    id SERIAL PRIMARY KEY,
    isbn VARCHAR(20) UNIQUE NOT NULL,
    title VARCHAR(500) NOT NULL,
    author_id INT NOT NULL REFERENCES authors(id),
    publisher_id INT REFERENCES publishers(id),
    category_id INT REFERENCES categories(id),
    published_year INT,
    page_count INT,
    language VARCHAR(20) DEFAULT 'English',
    copies_available INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE members (
    id SERIAL PRIMARY KEY,
    first_name VARCHAR(100) NOT NULL,
    last_name VARCHAR(100) NOT NULL,
    email VARCHAR(200) UNIQUE NOT NULL,
    phone VARCHAR(30),
    membership_date DATE NOT NULL DEFAULT CURRENT_DATE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE loans (
    id SERIAL PRIMARY KEY,
    book_id INT NOT NULL REFERENCES books(id),
    member_id INT NOT NULL REFERENCES members(id),
    loan_date TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    due_date DATE NOT NULL,
    return_date TIMESTAMP,
    late_fee DECIMAL(8,2) DEFAULT 0
);

CREATE TABLE reservations (
    id SERIAL PRIMARY KEY,
    book_id INT NOT NULL REFERENCES books(id),
    member_id INT NOT NULL REFERENCES members(id),
    reserved_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    status VARCHAR(20) NOT NULL DEFAULT 'pending'
);

COMMENT ON TABLE books IS 'Catalog of all physical books in the library';
COMMENT ON COLUMN books.copies_available IS 'Current number of copies available for loan';
COMMENT ON TABLE loans IS 'Records each book borrowed by a member';

INSERT INTO authors (first_name, last_name, nationality) VALUES
    ('George', 'Orwell', 'British'),
    ('Jane', 'Austen', 'British'),
    ('Haruki', 'Murakami', 'Japanese');

INSERT INTO publishers (name, country, founded_year) VALUES
    ('Penguin Books', 'UK', 1935),
    ('Vintage International', 'USA', 1954);

INSERT INTO categories (name) VALUES ('Fiction'), ('Classic'), ('Mystery');

INSERT INTO books (isbn, title, author_id, publisher_id, category_id, published_year, page_count, copies_available) VALUES
    ('978-0451524935', '1984', 1, 1, 1, 1949, 328, 5),
    ('978-0141439518', 'Pride and Prejudice', 2, 1, 2, 1813, 432, 3),
    ('978-0307476463', 'Kafka on the Shore', 3, 2, 1, 2002, 480, 2);

INSERT INTO members (first_name, last_name, email) VALUES
    ('Alice', 'Smith', 'alice@example.com'),
    ('Bob', 'Johnson', 'bob@example.com');
