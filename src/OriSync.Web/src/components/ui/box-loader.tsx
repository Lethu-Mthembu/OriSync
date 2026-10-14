import type React from 'react'

const BoxLoader: React.FC = () => {
  return (
    <div className="relative" aria-hidden="true">
      <div className="boxes">
        {[1, 2, 3, 4].map((box) => (
          <div className={`box box-${box}`} key={box}>
            <div className="face face-front" />
            <div className="face face-right" />
            <div className="face face-top" />
            <div className="face face-back" />
          </div>
        ))}
      </div>
    </div>
  )
}

export default BoxLoader
