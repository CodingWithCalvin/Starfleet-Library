import { sqliteTable, text, integer } from 'drizzle-orm/sqlite-core';
export const entries = sqliteTable('entries', {id:text('id').primaryKey(), revision:integer('revision').notNull(),book:text('book'),cover:text('cover')});
