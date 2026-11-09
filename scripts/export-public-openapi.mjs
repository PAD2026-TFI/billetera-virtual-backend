import { readFile, writeFile } from 'node:fs/promises'

const args = process.argv.slice(2)
const exportToMintlify = args[0] === '--mintlify'
const [sourcePath, apiBase] = exportToMintlify ? args.slice(1) : args
const portal = exportToMintlify ? 'mintlify' : 'scalar'
if (!sourcePath) {
  throw new Error('Uso: node scripts/export-public-openapi.mjs [--mintlify] <openapi.json> [URL HTTPS de la API]')
}

const document = JSON.parse(await readFile(sourcePath, 'utf8'))
const isPublicPath = path =>
  /^\/api\/v1\/(auth|me|integrations|public)\//.test(path) ||
  path === '/health/live' ||
  path === '/health/ready'

const merchantCheckoutOperations = {
  '/api/v1/integrations/register': 'post',
  '/api/v1/integrations/payment-links': 'post',
  '/api/v1/integrations/payment-links/{id}': 'get',
  '/api/v1/integrations/received-payments': 'get',
  '/api/v1/integrations/received-payments/{paymentId}': 'get',
  '/api/v1/public/payment-links/{token}': 'get',
  '/api/v1/public/payment-links/{token}/qr': 'get',
}

const paths = exportToMintlify
  ? Object.fromEntries(Object.entries(merchantCheckoutOperations).map(([path, method]) => {
    const { [method]: operation, parameters } = document.paths[path] ?? {}
    if (!operation) throw new Error(`Falta la operación de la pasarela: ${method.toUpperCase()} ${path}`)
    return [path, { ...(parameters ? { parameters } : {}), [method]: operation }]
  }))
  : Object.fromEntries(Object.entries(document.paths).filter(([path]) => isPublicPath(path)))
if (!Object.keys(paths).length) {
  throw new Error('El OpenAPI no contiene rutas del catálogo público de Wallet.')
}

const schemas = {}
const usedTags = new Set()
const schemaPrefix = '#/components/schemas/'

function collectReferences(value) {
  if (value === null || typeof value !== 'object') return
  if (Array.isArray(value.tags)) value.tags.forEach(tag => usedTags.add(tag))

  if (typeof value.$ref === 'string' && value.$ref.startsWith(schemaPrefix)) {
    const name = value.$ref.slice(schemaPrefix.length)
    if (!Object.hasOwn(schemas, name)) {
      const schema = document.components?.schemas?.[name]
      if (!schema) throw new Error(`Referencia a un esquema inexistente: ${value.$ref}`)
      schemas[name] = schema
      collectReferences(schema)
    }
  }

  Object.values(value).forEach(collectReferences)
}

collectReferences(paths)

const publicDocument = {
  openapi: document.openapi,
  info: {
    title: exportToMintlify ? 'Pasarela de pagos Padi' : document.info.title,
    version: document.info.version,
    description: exportToMintlify
      ? 'Referencia para comercios que integran el checkout alojado de Padi. '
        + 'Creá links de pago, dirigí al comprador al checkout y consultá los cobros desde tu servidor.'
      : 'Referencia de la billetera para clientes, comercios y checkout. '
        + 'Las operaciones de clientes usan JWT y las integraciones usan X-Api-Key. '
        + 'Consultá el esquema de seguridad de cada operación.',
  },
  paths,
  components: {
    schemas,
    securitySchemes: exportToMintlify
      ? { ApiKey: document.components.securitySchemes.ApiKey }
      : {
        ...document.components.securitySchemes,
        Bearer: { ...document.components.securitySchemes.Bearer, description: 'JWT del cliente.' },
      },
  },
  tags: (document.tags ?? []).filter(tag => usedTags.has(tag.name)),
}

if (apiBase) {
  const apiUrl = new URL(apiBase)
  if (apiUrl.protocol !== 'https:' || apiUrl.username || apiUrl.password ||
      apiUrl.pathname !== '/' || apiUrl.search || apiUrl.hash ||
      apiUrl.hostname === 'localhost' || apiUrl.hostname === '127.0.0.1' || apiUrl.hostname === '[::1]') {
    throw new Error('Indicá el origen HTTPS público de la API, sin credenciales ni rutas.')
  }
  publicDocument.servers = [{ url: apiUrl.origin, description: 'API publicada' }]
}

const targetPath = `deploy/${portal}/openapi.json`
const target = new URL(`../${targetPath}`, import.meta.url)
await writeFile(target, JSON.stringify(publicDocument, null, 2) + '\n')
console.log(`Exportadas ${Object.keys(paths).length} rutas en ${targetPath}`)
