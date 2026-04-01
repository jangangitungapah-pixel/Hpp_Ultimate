CREATE TABLE materials (
    id UUID PRIMARY KEY,
    code VARCHAR(32) NOT NULL UNIQUE,
    nama_material VARCHAR(200) NOT NULL,
    merk VARCHAR(120),
    base_unit VARCHAR(20) NOT NULL,
    net_qty NUMERIC(18,4) NOT NULL CHECK (net_qty > 0),
    net_unit VARCHAR(20) NOT NULL,
    harga_per_pack NUMERIC(18,2) NOT NULL CHECK (harga_per_pack > 0),
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    description TEXT,
    created_at TIMESTAMP NOT NULL,
    updated_at TIMESTAMP NOT NULL
);

CREATE TABLE material_unit_conversions (
    id UUID PRIMARY KEY,
    material_id UUID NOT NULL REFERENCES materials(id) ON DELETE CASCADE,
    unit_name VARCHAR(50) NOT NULL,
    conversion_qty NUMERIC(18,4) NOT NULL CHECK (conversion_qty > 0),
    UNIQUE (material_id, unit_name)
);

CREATE TABLE material_price_history (
    id UUID PRIMARY KEY,
    material_id UUID NOT NULL REFERENCES materials(id) ON DELETE CASCADE,
    effective_at TIMESTAMP NOT NULL,
    harga_per_pack NUMERIC(18,2) NOT NULL CHECK (harga_per_pack > 0),
    net_qty NUMERIC(18,4) NOT NULL CHECK (net_qty > 0),
    net_unit VARCHAR(20) NOT NULL,
    base_unit VARCHAR(20) NOT NULL,
    note VARCHAR(250) NOT NULL
);

CREATE TABLE gudang_stocks (
    id UUID PRIMARY KEY,
    warehouse_id UUID NOT NULL,
    material_id UUID NOT NULL REFERENCES materials(id),
    qty_on_hand NUMERIC(18,4) NOT NULL DEFAULT 0,
    updated_at TIMESTAMP NOT NULL,
    UNIQUE (warehouse_id, material_id)
);
