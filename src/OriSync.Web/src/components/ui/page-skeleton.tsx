import BoxLoader from '@/components/ui/box-loader'

type SkeletonVariant = 'dashboard' | 'table' | 'settings' | 'auth' | 'scanner'

interface PageSkeletonProps {
  label: string
  variant?: SkeletonVariant
  rows?: number
}

const pulse = 'animate-pulse rounded-md bg-[#dce5ea] motion-reduce:animate-none'

function Line({ className = '' }: { className?: string }) {
  return <div className={`${pulse} h-3 ${className}`} />
}

function DashboardSkeleton() {
  return (
    <>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {[1, 2, 3, 4].map((item) => <div className={`${pulse} h-28`} key={item} />)}
      </div>
      <div className="mt-5 rounded-lg border border-[#dce4e8] bg-white p-5">
        <Line className="w-40" />
        <Line className="mt-4 w-full" />
        <Line className="mt-3 w-5/6" />
        <Line className="mt-3 w-2/3" />
      </div>
    </>
  )
}

function TableSkeleton({ rows }: { rows: number }) {
  return (
    <div className="overflow-hidden rounded-lg border border-[#dce4e8] bg-white">
      <div className="grid grid-cols-4 gap-4 border-b border-[#dce4e8] bg-[#f5f8fa] p-4">
        {[1, 2, 3, 4].map((item) => <Line key={item} />)}
      </div>
      {Array.from({ length: rows }, (_, row) => (
        <div className="grid grid-cols-4 gap-4 border-b border-[#edf1f3] p-4 last:border-b-0" key={row}>
          <Line className="w-4/5" />
          <Line className="w-3/5" />
          <Line className="w-2/3" />
          <Line className="w-1/2" />
        </div>
      ))}
    </div>
  )
}

function SettingsSkeleton() {
  return (
    <div className="grid gap-4 rounded-lg border border-[#dce4e8] bg-white p-5">
      <Line className="h-5 w-48" />
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        {[1, 2, 3, 4].map((item) => <div className={`${pulse} h-12`} key={item} />)}
      </div>
      <Line className="w-3/4" />
      <Line className="w-1/2" />
    </div>
  )
}

function FocusedSkeleton({ scanner }: { scanner: boolean }) {
  return (
    <div className={`mx-auto grid w-full max-w-xl place-items-center rounded-xl border border-[#dce4e8] bg-white p-8 ${scanner ? 'min-h-[28rem]' : 'min-h-72'}`}>
      <div className="grid w-full place-items-center gap-7 text-center">
        <BoxLoader />
        <div className="grid w-full max-w-sm gap-3">
          <Line className="mx-auto h-5 w-3/5" />
          <Line className="mx-auto w-4/5" />
          <Line className="mx-auto w-2/3" />
        </div>
        {scanner && <div className={`${pulse} aspect-square w-full max-w-64 rounded-2xl`} />}
      </div>
    </div>
  )
}

export function PageSkeleton({ label, variant = 'table', rows = 5 }: PageSkeletonProps) {
  return (
    <section className="w-full" role="status" aria-live="polite" aria-busy="true">
      <span className="sr-only">{label}</span>
      <div aria-hidden="true">
        {variant === 'dashboard' && <DashboardSkeleton />}
        {variant === 'table' && <TableSkeleton rows={rows} />}
        {variant === 'settings' && <SettingsSkeleton />}
        {variant === 'auth' && <FocusedSkeleton scanner={false} />}
        {variant === 'scanner' && <FocusedSkeleton scanner />}
      </div>
    </section>
  )
}
