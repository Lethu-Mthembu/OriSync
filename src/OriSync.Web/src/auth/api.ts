interface ProblemDetails {
  detail?: string
  title?: string
}

interface CsrfResponse {
  requestToken: string
}

let csrfToken: string | null = null

async function getCsrfToken() {
  if (csrfToken) return csrfToken

  const response = await fetch('/api/auth/csrf', {
    credentials: 'same-origin',
    cache: 'no-store',
  })
  if (!response.ok) throw new Error('Security token could not be created.')

  const payload = (await response.json()) as CsrfResponse
  csrfToken = payload.requestToken
  return csrfToken
}

export async function postJson(
  path: string,
  body?: unknown,
  retryInvalidCsrf = true,
): Promise<Response> {
  const token = await getCsrfToken()
  const response = await fetch(path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: {
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': token,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (response.status === 400 && response.headers.get('content-type')?.includes('json')) {
    const problem = (await response.clone().json()) as ProblemDetails
    if (problem.title === 'Invalid CSRF token.') {
      csrfToken = null
      if (retryInvalidCsrf) return postJson(path, body, false)
    }
  }

  return response
}

export async function getError(response: Response) {
  try {
    const payload = (await response.json()) as ProblemDetails
    return payload.detail ?? payload.title ?? 'The request could not be completed.'
  } catch {
    return 'The request could not be completed.'
  }
}
