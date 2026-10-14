import React from 'react';
export function Card({ title, actions, children, padded = true, style, className = '', ...rest }) {
  return <section className={'ar-card ' + className} style={style} {...rest}>
    {(title || actions) && <header className="ar-card__head"><h3 className="ar-card__title">{title}</h3>{actions && <div style={{ display: 'flex', gap: 8 }}>{actions}</div>}</header>}
    {padded ? <div className="ar-card__body">{children}</div> : children}
  </section>;
}
