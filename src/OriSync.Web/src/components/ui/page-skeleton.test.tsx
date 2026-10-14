import '@testing-library/jest-dom/vitest'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { PageSkeleton } from './page-skeleton'

describe('PageSkeleton', () => {
  it('announces the loading state without exposing decorative blocks', () => {
    render(<PageSkeleton label="Loading students" variant="table" rows={3} />)

    const status = screen.getByRole('status')
    expect(status).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByText('Loading students')).toHaveClass('sr-only')
    expect(status.querySelectorAll('[aria-hidden="true"]')).toHaveLength(1)
  })

  it('renders the box loader in focused loading states', () => {
    const { container } = render(<PageSkeleton label="Loading scanner" variant="scanner" />)

    expect(container.querySelector('.boxes')).toBeInTheDocument()
    expect(container.querySelectorAll('.box')).toHaveLength(4)
  })
})
