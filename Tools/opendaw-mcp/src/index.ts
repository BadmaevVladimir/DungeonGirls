#!/usr/bin/env node
import {StdioServerTransport} from "@modelcontextprotocol/sdk/server/stdio.js"
import {fileURLToPath} from "node:url"
import {dirname, resolve} from "node:path"
import {createServer} from "./server"

const here = dirname(fileURLToPath(import.meta.url))
const server = createServer({
    hostDir: resolve(here, "../host/dist"),
    outputDir: process.env["OPENDAW_OUTPUT_DIR"] ?? resolve(process.cwd(), "Audio/Music")
})
await server.connect(new StdioServerTransport())
